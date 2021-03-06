﻿// Copyright 2020 Raising the Floor - International
//
// Licensed under the New BSD license. You may not use this file except in
// compliance with this License.
//
// You may obtain a copy of the License at
// https://github.com/GPII/universal/blob/master/LICENSE.txt
//
// The R&D leading to these results received funding from the:
// * Rehabilitation Services Administration, US Dept. of Education under 
//   grant H421A150006 (APCP)
// * National Institute on Disability, Independent Living, and 
//   Rehabilitation Research (NIDILRR)
// * Administration for Independent Living & Dept. of Education under grants 
//   H133E080022 (RERC-IT) and H133E130028/90RE5003-01-00 (UIITA-RERC)
// * European Union's Seventh Framework Programme (FP7/2007-2013) grant 
//   agreement nos. 289016 (Cloud4all) and 610510 (Prosperity4All)
// * William and Flora Hewlett Foundation
// * Ontario Ministry of Research and Innovation
// * Canadian Foundation for Innovation
// * Adobe Foundation
// * Consumer Electronics Association Foundation

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Logging;
using System.Runtime.InteropServices;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.Win32;
using System.Threading;
using System.Runtime.InteropServices.ComTypes;

namespace Morphic.Settings.SystemSettings
{
    /// <summary>
    /// A setting handler for Windows System Settings
    /// </summary>
    /// <remarks>
    /// Information about System Settings can be found in the Windows Registry under
    /// 
    /// HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\SystemSettings\SettingId\SomeSettingId
    /// 
    /// Each subkey has an value for DllPath, which contains a string of the absolute path to a DLL
    /// that in turn contains a GetSetting() function.
    /// 
    /// The result of calling GetSetting("SomeSettingId") is an object that has GetValue() and SetValue() methods,
    /// which read and write the setting, respectively.
    /// </remarks>
    public class SystemSettingHandler: SettingHandler
    {

        public Setting Setting { get; private set; }

        /// <summary>
        /// The handler description from the solution registry
        /// </summary>
        public SystemSettingHandlerDescription Description
        {
            get
            {
                return (Setting.HandlerDescription as SystemSettingHandlerDescription)!;
            }
        }

        /// <summary>
        /// The system setting instance that does most of the work
        /// </summary>
        private readonly ISystemSetting systemSetting;

        /// <summary>
        /// Create a new system settings handler with the given description and logger
        /// </summary>
        /// <param name="description"></param>
        /// <param name="logger"></param>
        public SystemSettingHandler(Setting setting, ISystemSettingFactory systemSettingFactory, IServiceProvider serviceProvider, ILogger<SystemSettingHandler> logger)
        {
            Setting = setting;
            systemSetting = systemSettingFactory.Create(Description.SettingId, serviceProvider);
            this.logger = logger;
        }

        /// <summary>
        /// The logger to use
        /// </summary>
        private readonly ILogger<SystemSettingHandler> logger;

        /// <summary>
        /// Apply the value to the setting
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        public override async Task<bool> Apply(object? value)
        {
            if (TryConvertToSystem(value, Description.ValueKind, out var systemValue))
            {
                try
                {
                    await systemSetting.SetValue(systemValue!);
                    return true;
                }catch (Exception e)
                {
                    logger.LogError(e, "Failed to set system setting {0}", Description.SettingId);
                    return false;
                }
            }
            else
            {
                logger.LogError("Type mismatch on Apply for {0}", Description.SettingId);
                return false;
            }
        }

        public override async Task<CaptureResult> Capture()
        {
            var result = new CaptureResult();
            try
            {
                var systemValue = await systemSetting.GetValue();
                result.Success = TryConvertFromSystem(systemValue, Setting.Kind, out result.Value);
            }
            catch(Exception e)
            {
                logger.LogError(e, "Failed to GetValue() for {0}", Description.SettingId);
            }
            return result;

        }

        public bool TryConvertToSystem(object? value, SystemValueKind systemValueKind, out object? systemValue)
        {
            if (value is string stringValue)
            {
                switch (systemValueKind)
                {
                    case SystemValueKind.String:
                        systemValue = stringValue;
                        return true;
                }
                systemValue = null;
                return false;
            }
            if (value is bool boolValue)
            {
                switch (systemValueKind)
                {
                    case SystemValueKind.Boolean:
                        systemValue = boolValue;
                        return true;
                }
                systemValue = null;
                return false;
            }
            if (value is long longValue)
            {
                switch (systemValueKind)
                {
                    case SystemValueKind.Integer:
                        systemValue = (UInt32)longValue;
                        return true;
                    case SystemValueKind.IdPrefixedEnum:
                        systemValue = String.Format("{0}{1}", Description.SettingId, longValue);
                        return true;
                    case SystemValueKind.String:
                        if (Description.IntegerMap is string[] map)
                        {
                            if (longValue >= 0 && longValue < map.Length)
                            {
                                systemValue = map[longValue];
                                return true;
                            }
                        }
                        systemValue = null;
                        return false;
                }
                systemValue = null;
                return false;
            }
            systemValue = null;
            return false;
        }

        public bool TryConvertFromSystem(object? systemValue, Setting.ValueKind valueKind, out object? resultValue)
        {
            if (systemValue is string stringValue)
            {
                switch (valueKind)
                {
                    case Setting.ValueKind.String:
                        resultValue = stringValue;
                        return true;
                    case Setting.ValueKind.Integer:
                        if (Description.ValueKind == SystemValueKind.IdPrefixedEnum)
                        {
                            if (stringValue.StartsWith(Description.SettingId))
                            {
                                if (long.TryParse(stringValue.Substring(Description.SettingId.Length), out var longValue))
                                {
                                    resultValue = longValue;
                                    return true;
                                }
                            }
                        }
                        else if (Description.ReverseIntegerMap is Dictionary<string, long> map)
                        {
                            if (map.TryGetValue(stringValue, out long longValue))
                            {
                                resultValue = longValue;
                                return true;
                            }
                        }
                        resultValue = null;
                        return false;
                }
                resultValue = null;
                return false;
            }
            if (systemValue is bool boolValue)
            {
                switch (valueKind)
                {
                    case Setting.ValueKind.Boolean:
                        resultValue = boolValue;
                        return true;
                }
                resultValue = null;
                return false;
            }
            if (systemValue is UInt32 intValue)
            {
                switch (valueKind)
                {
                    case Setting.ValueKind.Integer:
                        resultValue = (long)intValue;
                        return true;
                }
                resultValue = null;
                return false;
            }
            if (systemValue != null)
            {
                logger.LogDebug("Got type {0} from system for {1}", systemValue.GetType().Name, Description.SettingId);
            }
            else
            {
                logger.LogDebug("Got null from system for {1}", Description.SettingId);
            }
            resultValue = null;
            return false;
        }

    }
}
