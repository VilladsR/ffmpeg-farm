﻿using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Transactions;
using System.Web.Http;
using Contract;
using Dapper;

namespace API.WindowsService.Controllers
{
    public class StatusController : ApiController
    {
        public JobResult GetStatus()
        {
            IEnumerable<dynamic> jobs;
            IEnumerable<JobResultModel> requests;
            using (var connection = Helper.GetConnection())
            {
                connection.Open();
                requests = connection.Query<JobResultModel>("SELECT * from FfmpegRequest").ToList();
                jobs = connection.Query<TranscodingJob>("SELECT * FROM FfmpegJobs").ToList();
            }

            foreach (dynamic request in requests)
            {
                request.Jobs = jobs.Where(x => x.JobCorrelationId == request.JobCorrelationId);
            }

            return new JobResult
            {
                Requests = requests
            };
        }

        public void PutProgressUpdate(BaseJob job)
        {
            if (job == null) throw new ArgumentNullException(nameof(job));
            if (string.IsNullOrWhiteSpace(job.MachineName))
            {
                throw new HttpResponseException(new HttpResponseMessage
                {
                    ReasonPhrase = "Machinename must be specified",
                    StatusCode = HttpStatusCode.BadRequest
                });
            }

            Helper.InsertClientHeartbeat(job.MachineName);

            using (var connection = Helper.GetConnection())
            {
                connection.Open();

                JobRequest jobRequest = connection.Query<JobRequest>(
                    "SELECT JobCorrelationId, VideoSourceFilename, AudioSourceFilename, DestinationFilename, Needed, EnableDash FROM FfmpegRequest WHERE JobCorrelationId = @Id",
                    new {Id = job.JobCorrelationId})
                    .SingleOrDefault();
                if (jobRequest == null)
                    throw new ArgumentException($@"Job with correlation id {job.JobCorrelationId} not found");

                jobRequest.Targets = connection.Query<DestinationFormat>(
                    "SELECT JobCorrelationId, Width, Height, VideoBitrate, AudioBitrate FROM FfmpegRequestTargets WHERE JobCorrelationId = @Id;",
                    new {Id = job.JobCorrelationId})
                    .ToArray();

                using (var scope = new TransactionScope())
                {
                    Type jobType = job.GetType();
                    TranscodingJobState jobState = job.Done
                        ? TranscodingJobState.Done
                        : TranscodingJobState.InProgress;

                    if (jobType == typeof(TranscodingJob))
                    {
                        int updatedRows = connection.Execute(
                            "UPDATE FfmpegJobs SET Progress = @Progress, Heartbeat = @Heartbeat, State = @State WHERE Id = @Id;",
                            new
                            {
                                Id = job.Id,
                                Progress = job.Progress.TotalSeconds,
                                Heartbeat = DateTimeOffset.UtcNow.UtcDateTime,
                                State = jobState
                            });

                        if (updatedRows != 1)
                            throw new Exception($"Failed to update progress for job id {job.Id}");
                    }
                    else if (jobType == typeof(MergeJob))
                    {
                        int updatedRows = connection.Execute(
                            "UPDATE FfmpegMergeJobs SET Progress = @Progress, Heartbeat = @Heartbeat, State = @State WHERE Id = @Id;",
                            new
                            {
                                Id = job.Id,
                                Progress = job.Progress.TotalSeconds,
                                Heartbeat = DateTimeOffset.UtcNow.UtcDateTime,
                                State = jobState
                            });

                        if (updatedRows != 1)
                            throw new Exception($"Failed to update progress for job id {job.Id}");

                        int target = connection.Query<int>("SELECT Target FROM FfmpegMergeJobs WHERE Id = @Id;",
                            new {Id = job.Id})
                            .Single();

                        if (jobState == TranscodingJobState.Done)
                        {
                            IEnumerable<FfmpegPart> parts = connection.Query<FfmpegPart>(
                                "SELECT Filename FROM FfmpegParts WHERE JobCorrelationId = @JobCorrelationId AND Target = @Target ORDER BY Target, Number;",
                                new {JobCorrelationId = job.JobCorrelationId, Target = target});

                            foreach (FfmpegPart part in parts)
                            {
                                File.Delete(part.Filename);
                            }
                        }
                    }
                    else if (jobType == typeof(Mp4boxJob))
                    {
                        int updatedRows = connection.Execute(
                            "UPDATE Mp4boxJobs SET Heartbeat = @Heartbeat, State = @State WHERE JobCorrelationId = @Id;",
                            new
                            {
                                Id = job.JobCorrelationId,
                                Progress = job.Progress.TotalSeconds,
                                Heartbeat = DateTimeOffset.UtcNow.UtcDateTime,
                                State = jobState
                            });

                        if (updatedRows != 1)
                            throw new Exception($"Failed to update progress for job id {job.Id}");
                    }
                    
                    scope.Complete();
                }

                using (var scope = new TransactionScope())
                {
                    ICollection<TranscodingJobState> totalJobs = connection.Query(
                            "SELECT State FROM FfmpegJobs WHERE JobCorrelationId = @Id;",
                            new { Id = jobRequest.JobCorrelationId })
                            .Select(x => (TranscodingJobState)Enum.Parse(typeof(TranscodingJobState), x.State))
                            .ToList();

                    if (totalJobs.Any(x => x != TranscodingJobState.Done))
                    {
                        // Not all transcoding jobs are finished
                        return;
                    }

                    totalJobs = connection.Query(
                        "SELECT State FROM FfmpegMergeJobs WHERE JobCorrelationId = @Id;",
                        new {Id = jobRequest.JobCorrelationId})
                        .Select(x => (TranscodingJobState) Enum.Parse(typeof(TranscodingJobState), x.State))
                        .ToList();

                    if (totalJobs.Any(x => x != TranscodingJobState.Done))
                    {
                        // Not all merge jobs are finished
                        return;
                    }

                    string destinationFilename = jobRequest.DestinationFilename;
                    string fileNameWithoutExtension =
                        Path.GetFileNameWithoutExtension(destinationFilename);
                    string fileExtension = Path.GetExtension(destinationFilename);
                    string outputFolder = Path.GetDirectoryName(destinationFilename);

                    if (totalJobs.Count == 0)
                    {
                        QueueMergeJob(job, connection, outputFolder, fileNameWithoutExtension, fileExtension, jobRequest);
                    }
                    else if (jobRequest.EnableDash)
                    {
                        QueueMpegDashMergeJob(job, destinationFilename, connection, jobRequest, fileNameWithoutExtension, outputFolder, fileExtension);
                    }

                    scope.Complete();
                }
            }
        }

        private static void QueueMpegDashMergeJob(BaseJob job, string destinationFilename, IDbConnection connection,
            JobRequest jobRequest, string fileNameWithoutExtension, string outputFolder, string fileExtension)
        {
            string destinationFolder = Path.GetDirectoryName(destinationFilename);
            ICollection<TranscodingJobState> totalJobs =
                connection.Query("SELECT State FROM Mp4boxJobs WHERE JobCorrelationId = @Id;",
                    new {Id = jobRequest.JobCorrelationId})
                    .Select(x => (TranscodingJobState) Enum.Parse(typeof(TranscodingJobState), x.State))
                    .ToList();

            // One MPEG DASH merge job is already queued. Do nothing
            if (totalJobs.Any())
                return;

            string arguments =
                $@"-dash 4000 -rap -frag-rap -profile onDemand -out {destinationFolder}{Path.DirectorySeparatorChar}{fileNameWithoutExtension}.mpd";

            var chunks = connection.Query<FfmpegPart>(
                "SELECT Filename, Number, Target, (SELECT VideoSourceFilename FROM FfmpegRequest WHERE JobCorrelationId = @Id) AS VideoSourceFilename FROM FfmpegParts WHERE JobCorrelationId = @Id ORDER BY Target, Number;",
                new {Id = job.JobCorrelationId});
            foreach (var chunk in chunks.GroupBy(x => x.Target, x => x, (key, values) => values))
            {
                int targetNumber = chunk.First().Target;
                DestinationFormat target = jobRequest.Targets[targetNumber];

                string targetFilename =
                    $@"{outputFolder}{Path.DirectorySeparatorChar}{fileNameWithoutExtension}_{target.Width}x{target
                        .Height}_{target.VideoBitrate}_{target.AudioBitrate}{fileExtension}";

                arguments += $@" {targetFilename}";
            }

            connection.Execute(
                "INSERT INTO Mp4boxJobs (JobCorrelationId, Arguments, Needed, State) VALUES(@JobCorrelationId, @Arguments, @Needed, @State);",
                new
                {
                    JobCorrelationId = jobRequest.JobCorrelationId,
                    Arguments = arguments,
                    Needed = jobRequest.Needed,
                    State = TranscodingJobState.Queued
                });
        }

        private static void QueueMergeJob(BaseJob job, IDbConnection connection, string outputFolder,
            string fileNameWithoutExtension, string fileExtension, JobRequest jobRequest)
        {
            var chunks = connection.Query<FfmpegPart>(
                "SELECT Filename, Number, Target, (SELECT VideoSourceFilename FROM FfmpegRequest WHERE JobCorrelationId = @Id) AS VideoSourceFilename FROM FfmpegParts WHERE JobCorrelationId = @Id ORDER BY Target, Number;",
                new {Id = job.JobCorrelationId});

            foreach (IEnumerable<FfmpegPart> chunk in chunks.GroupBy(x => x.Target, x => x, (key, values) => values))
            {
                var ffmpegParts = chunk as IList<FfmpegPart> ?? chunk.ToList();
                int targetNumber = ffmpegParts.First().Target;
                DestinationFormat target = jobRequest.Targets[targetNumber];

                string targetFilename =
                    $@"{outputFolder}{Path.DirectorySeparatorChar}{fileNameWithoutExtension}_{target.Width}x{target.Height}_{target.VideoBitrate}_{target.AudioBitrate}{fileExtension}";

                // TODO Implement proper detection if files are already merged
                if (File.Exists(targetFilename))
                    continue;

                string path = string.Format("{0}{1}{2}_{3}.list",
                    outputFolder,
                    Path.DirectorySeparatorChar,
                    fileNameWithoutExtension,
                    targetNumber);

                using (TextWriter tw = new StreamWriter(path))
                {
                    foreach (FfmpegPart part in ffmpegParts.Where(x => x.IsAudio == false))
                    {
                        tw.WriteLine($"file '{part.Filename}'");
                    }
                }
                string audioSource = ffmpegParts.Single(x => x.IsAudio).Filename;

                string arguments =
                    $@"-y -f concat -safe 0 -i ""{path}"" -i ""{audioSource}"" -c copy {targetFilename}";

                int duration = Helper.GetDuration(jobRequest.VideoSourceFilename);
                connection.Execute(
                    "INSERT INTO FfmpegMergeJobs (JobCorrelationId, Arguments, Needed, State, Target) VALUES(@JobCorrelationId, @Arguments, @Needed, @State, @Target);",
                    new
                    {
                        JobCorrelationId = job.JobCorrelationId,
                        Arguments = arguments,
                        Needed = jobRequest.Needed,
                        State = TranscodingJobState.Queued,
                        Target = targetNumber
                    });
            }
        }
    }
}