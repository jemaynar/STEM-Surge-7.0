﻿/*
 * Copyright 2019 STEM Management
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *   http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 * 
 */

using System;
using System.Collections.Generic;
using System.ComponentModel;

namespace STEM.Surge.SQLServer
{
    [TypeConverter(typeof(ExpandableObjectConverter))]
    [DisplayName("ExecuteNonQuery")]
    [Description("Execute an action against a database (i.e. insert, update, delete).")]
    public class ExecuteNonQuery : SQLBaseInstruction
    {
        [DisplayName("Sql to be executed"), DescriptionAttribute("This is the Sql that will be executed.")]
        public List<string> Sql { get; set; }

        [DisplayName("Retry Attempts")]
        public int Retry { get; set; }

        public ExecuteNonQuery()
        {
            Retry = 3;
            Sql = new List<string>();
        }

        protected override void _Rollback()
        {
            // No rollback
        }

        protected override bool _Run()
        {
            try
            {
                base.ExecuteNonQuery(String.Join("\r\n", Sql), Retry);

                return true;
            }
            catch (Exception ex)
            {
                Exceptions.Add(ex);
                return false;
            }
        }
    }
}