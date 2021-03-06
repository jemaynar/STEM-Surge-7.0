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
using FluentFTP;

namespace STEM.Surge.FTP
{
    [TypeConverter(typeof(ExpandableObjectConverter))]
    [DisplayName("Rename")]
    [Description("Rename a file on an FTP server.")]
    public class Rename : STEM.Surge.Instruction
    {
        [Category("FTP Server")]
        [DisplayName("Authentication"), DescriptionAttribute("The authentication configuration to be used.")]
        public Authentication Authentication { get; set; }

        [Category("FTP Server")]
        [DisplayName("FTP Server Address"), DescriptionAttribute("What is the FTP Server Address?")]
        public string ServerAddress { get; set; }

        [Category("FTP Server")]
        [DisplayName("FTP Port"), DescriptionAttribute("What is the FTP Port?")]
        public string Port { get; set; }

        [Category("Target")]
        [DisplayName("Source File"), DescriptionAttribute("The full path of the file to be renamed.")]
        public string SourceFile { get; set; }

        [Category("Target")]
        [DisplayName("New File"), DescriptionAttribute("The full path of the new file.")]
        public string NewFile { get; set; }

        [DisplayName("File Exists Action")]
        [Description("What action should be taken if the Destination File already exists?")]
        public STEM.Sys.IO.FileExistsAction FileExistsAction { get; set; }

        [Category("Retry")]
        [DisplayName("Number of retries"), DescriptionAttribute("How many times should each operation be attempted?")]
        public int Retry { get; set; }

        [Category("Retry")]
        [DisplayName("Seconds between retries"), DescriptionAttribute("How many seconds should we wait between retries?")]
        public int RetryDelaySeconds { get; set; }

        public Rename()
        {
            Authentication = new Authentication();
            ServerAddress = "[FtpServerAddress]";
            Port = "[FtpServerPort]";

            SourceFile = "[TargetPath]\\[TargetName]";
            NewFile = "[TargetPath]\\NewFileName.txt";

            FileExistsAction = STEM.Sys.IO.FileExistsAction.MakeUnique;

            Retry = 1;
            RetryDelaySeconds = 2;
        }

        protected override void _Rollback()
        {
        }

        protected override bool _Run()
        {
            int r = Retry;

            while (r-- >= 0)
                try
                {
                    string address = Authentication.NextAddress(ServerAddress);

                    if (address == null)
                    {
                        Exception ex = new Exception("No valid address. (" + ServerAddress + ")");
                        Exceptions.Add(ex);
                        AppendToMessage(ex.Message);
                        return false;
                    }

                    FtpClient conn = Authentication.OpenClient(address, Int32.Parse(Port));

                    try
                    {
                        string file = Authentication.AdjustPath(address, SourceFile);
                        string directory = Authentication.AdjustPath(address, STEM.Sys.IO.Path.GetDirectoryName(NewFile));
                        
                        if (!conn.FileExists(file))
                            throw new System.IO.IOException("The target file does not exist: (" + SourceFile + ")");

                        string dst = System.IO.Path.Combine(directory, NewFile);
                                                
                        if (conn.FileExists(dst))
                            switch (FileExistsAction)
                            {
                                case STEM.Sys.IO.FileExistsAction.Skip:
                                    return true;

                                case STEM.Sys.IO.FileExistsAction.Throw:
                                    r = -1;
                                    throw new System.IO.IOException("Destination file exists. (" + dst + ")");

                                case STEM.Sys.IO.FileExistsAction.Overwrite:
                                case Sys.IO.FileExistsAction.OverwriteIfNewer:
                                    conn.DeleteFile(dst);
                                    break;

                                case STEM.Sys.IO.FileExistsAction.MakeUnique:
                                    dst = Authentication.UniqueFilename(address, Int32.Parse(Port), dst);
                                    break;
                            }
                        
                        if (!conn.DirectoryExists(directory))
                            conn.CreateDirectory(directory);

                        conn.Rename(file, dst);

                        AppendToMessage(file + " renamed to " + dst);
                    }
                    finally
                    {
                        Authentication.RecycleClient(conn);
                    }

                    break;
                }
                catch (Exception ex)
                {
                    if (r < 0)
                    {
                        AppendToMessage(ex.Message);
                        Exceptions.Add(ex);
                    }
                    else
                    {
                        System.Threading.Thread.Sleep(RetryDelaySeconds * 1000);
                    }
                }

            return Exceptions.Count == 0;
        }
    }
}
