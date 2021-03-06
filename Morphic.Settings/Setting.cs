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
using System.IO;
using System.Threading.Tasks;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Morphic.Core;
using Microsoft.Win32;

namespace Morphic.Settings
{

    /// <summary>
    /// A single setting within a solution
    /// </summary>
    public class Setting
    {

        /// <summary>
        /// The possible value types for a solution
        /// </summary>
        public enum ValueKind
        {
            String,
            Boolean,
            Integer,
            Double,
            Files
        }

        /// <summary>
        /// The name of the setting
        /// </summary>
        /// <remarks>
        /// Together with the owning solution id, this name can be used to
        /// create a <code>Preferences.Key</code>
        /// </remarks>
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        /// <summary>
        /// The kind of value this setting takes
        /// </summary>
        [JsonPropertyName("type")]
        public ValueKind Kind { get; set; }

        /// <summary>
        /// The default value for this setting
        /// </summary>
        [JsonPropertyName("default")]
        public object? Default { get; set; }

        /// <summary>
        /// The description of the handler for this setting
        /// </summary>
        [JsonPropertyName("handler")]
        public SettingHandlerDescription? HandlerDescription { get; set; }

        /// <summary>
        /// The description of the handler for this setting
        /// </summary>
        [JsonPropertyName("finalizer")]
        public SettingFinalizerDescription? FinalizerDescription { get; set; }

        public bool isDefault(object? value)
        {
            return EqualValues(Kind, value, Default);
        }

        public static bool EqualValues(ValueKind kind, object? value1, object? value2)
        {
            if (value1 == null && value2 == null)
            {
                return true;
            }
            switch (kind)
            {
                case ValueKind.Boolean:
                    {
                        if (value1 is bool boolValue1)
                        {
                            if (value2 is bool boolDefault2)
                            {
                                return boolValue1 == boolDefault2;
                            }
                        }
                        return false;
                    }
                case ValueKind.Double:
                    {
                        double doubleValue1;
                        double doubleValue2;
                        if (value1 is double doubleValue1_)
                        {
                            doubleValue1 = doubleValue1_;
                        }
                        else if (value1 is long longValue1)
                        {
                            doubleValue1 = (double)longValue1;
                        }
                        else
                        {
                            return false;
                        }
                        if (value2 is double doubleValue2_)
                        {
                            doubleValue2 = doubleValue2_;
                        }
                        else if (value2 is long longValue2)
                        {
                            doubleValue2 = (double)longValue2;
                        }
                        else
                        {
                            return false;
                        }
                        return Math.Abs(doubleValue1 - doubleValue2) < 0.1;
                    }
                case ValueKind.Integer:
                    {
                        if (value1 is long longValue1)
                        {
                            if (value2 is long longValue2)
                            {
                                return longValue1 == longValue2;
                            }
                        }
                        return false;
                    }
                case ValueKind.String:
                    {
                        if (value1 is string stringValue1)
                        {
                            if (value2 is string stringValue2)
                            {
                                return stringValue1 == stringValue2;
                            }
                        }
                        return false;
                    }
                default:
                    return false;
            }
        }
    }

}
