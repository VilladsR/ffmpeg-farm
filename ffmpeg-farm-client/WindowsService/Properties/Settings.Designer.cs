﻿//------------------------------------------------------------------------------
// <auto-generated>
//     This code was generated by a tool.
//     Runtime Version:4.0.30319.42000
//
//     Changes to this file may cause incorrect behavior and will be lost if
//     the code is regenerated.
// </auto-generated>
//------------------------------------------------------------------------------

namespace FFmpegFarm.WindowsService.Properties {
    
    
    [global::System.Runtime.CompilerServices.CompilerGeneratedAttribute()]
    [global::System.CodeDom.Compiler.GeneratedCodeAttribute("Microsoft.VisualStudio.Editors.SettingsDesigner.SettingsSingleFileGenerator", "14.0.0.0")]
    internal sealed partial class Settings : global::System.Configuration.ApplicationSettingsBase {
        
        private static Settings defaultInstance = ((Settings)(global::System.Configuration.ApplicationSettingsBase.Synchronized(new Settings())));
        
        public static Settings Default {
            get {
                return defaultInstance;
            }
        }
        
        [global::System.Configuration.ApplicationScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("4")]
        public int Threads {
            get {
                return ((int)(this["Threads"]));
            }
        }
        
        [global::System.Configuration.ApplicationScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("\\\\ondnas01.net.dr.dk\\MediaCache\\ffmpeg-farm\\logfiles")]
        public string FFmpegLogPath {
            get {
                return ((string)(this["FFmpegLogPath"]));
            }
        }
        
        [global::System.Configuration.ApplicationScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.SpecialSettingAttribute(global::System.Configuration.SpecialSetting.WebServiceUrl)]
        [global::System.Configuration.DefaultSettingValueAttribute("http://od01udv.net.dr.dk:9000")]
        public string ControllerApi {
            get {
                return ((string)(this["ControllerApi"]));
            }
        }
        
        [global::System.Configuration.ApplicationScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("\\\\ondnas01.net.dr.dk\\MediaCache\\ffmpeg-farm\\ffmpeg-3.2-win64-static\\bin\\ffmpeg.ex" +
            "e")]
        public string FFmpegPath {
            get {
                return ((string)(this["FFmpegPath"]));
            }
        }
        
        [global::System.Configuration.ApplicationScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("FC_CONFIG_DIR=\\\\ondnas01\\MediaCache;FONTCONFIG_FILE=\\\\ondnas01\\MediaCache\\fonts.c" +
            "onf;FONTCONFIG_PATH=\\\\ondnas01\\MediaCache")]
        public string EnvorimentVars {
            get {
                return ((string)(this["EnvorimentVars"]));
            }
        }
    }
}
