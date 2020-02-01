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
using System.IO;
using System.Linq;
using System.Xml.Serialization;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.ComponentModel;
using STEM.Sys.Security;

namespace STEM.Surge.BasicControllers
{
    [TypeConverter(typeof(ExpandableObjectConverter))]
    [DisplayName("Basic File Controller")]
    [Description("Customize an InstructionSet Template using placeholders related to the properties for each file or directory discovered. (e.g. [TargetPath], [TargetName], [LastWriteTimeUtc]...) " +
        "Note that with this controller [DestinationPath] can be an expandable path where the controller will choose the best path based on network proximity and destination directory existence (if 'Check for directory existence' is enabled). " +
        "Files from this controller are addressed in alphabetical order unless 'Randomize List' is set to true.")]
    public class BasicFileController : STEM.Surge.FileDeploymentController
    {
        [Category("Destination Path")]
        [DisplayName("Destination Path Impersonation user"), DescriptionAttribute("What user should be impersonated to access the Destination Path (if any)?")]
        public string DestinationPathUser { get; set; }

        [Category("Destination Path")]
        [DisplayName("Destination Path Impersonation Password"), DescriptionAttribute("What is the impersonation password?")]
        [XmlIgnore]
        [PasswordPropertyText(true)]
        public string DestinationPathPassword { get; set; }
        [Browsable(false)]
        public string DestinationPathPasswordEncoded
        {
            get
            {
                return this.Entangle(DestinationPathPassword);
            }

            set
            {
                DestinationPathPassword = this.Detangle(value);
            }
        }

        [Category("Destination Path")]
        [DisplayName("Destination Path Impersonation User Is Local"), DescriptionAttribute("Is the impersonated user local?")]
        public bool DestinationPathImpersonationIsLocal { get; set; }

        [Category("Destination Path")]
        [DisplayName("Check for directory existence"), DescriptionAttribute("Should the Controller verify the existence of the destination path before assigning?")]
        public bool CheckDirectoryExists { get; set; }

        [DisplayName("Randomize List"), DescriptionAttribute("Should the Controller randomize the listing in ListPreprocess?")]
        public bool RandomizeList { get; set; }
               
        public BasicFileController()
        {
            TemplateKVP["[DestinationPath]"] = "D:\\Data";
            TemplateKVP["[DestinationAddress]"] = "Reserved";

            CheckDirectoryExists = false;
            RandomizeList = false;
        }

        public override List<string> ListPreprocess(IReadOnlyList<string> list)
        {
            if (RandomizeList)
            {
                Random rnd = new Random();
                return list.OrderBy(i => rnd.Next()).ToList();
            }

            return new List<string>(list);
        }

        Random _Random = new Random();

        class Touched
        {
            public DateTime LastAttempt { get; set; }
            public bool Exists { get; set; }
        }

        class Used
        {
            public DateTime LastUsed { get; set; }
            public string Path { get; set; }
            public string IP { get; set; }
        }

        public override void CustomizeInstructionSet(_InstructionSet iSetTemplate, Dictionary<string, string> map, string branchIP, string initiationSource, bool cloneMap = true)
        {
            System.Collections.Generic.Dictionary<string, string> kvp = map;

            if (cloneMap)
                kvp = map.ToDictionary(i => i.Key, i => i.Value);

            string dest = null;

            string destPathMacro = kvp["[DestinationPath]"].Trim();

            if (destPathMacro.Length > 0)
                destPathMacro = base.ApplyKVP(destPathMacro, kvp, false);
            
            if (destPathMacro.Length > 0)
            {
                List<string> validDestPaths = STEM.Sys.IO.Path.ExpandRangedPath(destPathMacro);

                if (validDestPaths.Count == 1)
                {
                    dest = destPathMacro;

                    if (!_LastUsed.Exists(i => i.Path.Equals(dest, StringComparison.InvariantCultureIgnoreCase)))
                        _LastUsed.Add(new Used { IP = STEM.Sys.IO.Path.IPFromPath(dest), Path = dest, LastUsed = DateTime.UtcNow });

                    if (!GoodDirectory(dest))
                        dest = null;
                }
                else
                {
                    foreach (string s in validDestPaths)
                        if (!_LastUsed.Exists(i => i.Path.Equals(s, StringComparison.InvariantCultureIgnoreCase)))
                            _LastUsed.Add(new Used { IP = STEM.Sys.IO.Path.IPFromPath(s), Path = s, LastUsed = DateTime.UtcNow });

                    string first3 = STEM.Sys.IO.Path.GetFileNameWithoutExtension(branchIP);

                    foreach (Used u in _LastUsed.Where(i => validDestPaths.Exists(x => x.Equals(i.Path, StringComparison.InvariantCultureIgnoreCase)) && i.IP.StartsWith(first3)).OrderBy(i => i.LastUsed))
                        if (GoodDirectory(u.Path))
                        {
                            dest = u.Path;
                            u.LastUsed = DateTime.UtcNow;
                            break;
                        }

                    if (dest == null)
                    {
                        first3 = STEM.Sys.IO.Path.GetFileNameWithoutExtension(STEM.Sys.IO.Path.IPFromPath(initiationSource));

                        foreach (Used u in _LastUsed.Where(i => validDestPaths.Exists(x => x.Equals(i.Path, StringComparison.InvariantCultureIgnoreCase)) && i.IP.StartsWith(first3)).OrderBy(i => i.LastUsed))
                            if (GoodDirectory(u.Path))
                            {
                                dest = u.Path;
                                u.LastUsed = DateTime.UtcNow;
                                break;
                            }

                        if (dest == null)
                        {
                            string first2 = STEM.Sys.IO.Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(branchIP));

                            foreach (Used u in _LastUsed.Where(i => validDestPaths.Exists(x => x.Equals(i.Path, StringComparison.InvariantCultureIgnoreCase)) && i.IP.StartsWith(first2)).OrderBy(i => i.LastUsed))
                                if (GoodDirectory(u.Path))
                                {
                                    dest = u.Path;
                                    u.LastUsed = DateTime.UtcNow;
                                    break;
                                }

                            if (dest == null)
                            {
                                first2 = STEM.Sys.IO.Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(STEM.Sys.IO.Path.IPFromPath(initiationSource)));

                                foreach (Used u in _LastUsed.Where(i => validDestPaths.Exists(x => x.Equals(i.Path, StringComparison.InvariantCultureIgnoreCase)) && i.IP.StartsWith(first2)).OrderBy(i => i.LastUsed))
                                    if (GoodDirectory(u.Path))
                                    {
                                        dest = u.Path;
                                        u.LastUsed = DateTime.UtcNow;
                                        break;
                                    }
                            }

                            if (dest == null)
                                foreach (Used u in _LastUsed.Where(i => validDestPaths.Exists(x => x.Equals(i.Path, StringComparison.InvariantCultureIgnoreCase))).OrderBy(i => i.LastUsed))
                                    if (GoodDirectory(u.Path))
                                    {
                                        dest = u.Path;
                                        u.LastUsed = DateTime.UtcNow;
                                        break;
                                    }
                        }
                    }
                }
            }

            if (dest == null && (iSetTemplate.InstructionsXml.ToString().ToUpper().Contains("[DestinationPath]".ToUpper()) || iSetTemplate.InstructionsXml.ToString().ToUpper().Contains("[DestinationAddress]".ToUpper())))
                throw new Exception("No destination path was found to be acceptable. (" + TemplateKVP["[DestinationPath]"] + ")");

            if (dest != null)
            {
                kvp["[DestinationPath]"] = dest;
                kvp["[DestinationAddress]"] = STEM.Sys.IO.Path.IPFromPath(dest);
            }
            else
            {
                kvp["[DestinationPath]"] = "";
                kvp["[DestinationAddress]"] = "";
            }

            base.CustomizeInstructionSet(iSetTemplate, kvp, branchIP, initiationSource, false);
        }

        class LockOwner : STEM.Sys.State.IKeyLockOwner
        {
            static Dictionary<string, List<LockOwner>> _Locks = new Dictionary<string, List<LockOwner>>();

            BasicFileController _Controller = null;
            string _InitiationSource = null;
            string _Key = null;
            DateTime _LockTime = DateTime.MaxValue;
            List<LockOwner> _MemberOf = null;

            private LockOwner(BasicFileController controller, string initiationSource)
            {
                _Controller = controller;
                _InitiationSource = initiationSource;
                _Key = STEM.Sys.IO.Path.GetFileName(_InitiationSource);
                _LockTime = DateTime.UtcNow;

                if (!_Locks.ContainsKey(_Controller.DeploymentControllerID))
                    lock (_Locks)
                        if (!_Locks.ContainsKey(_Controller.DeploymentControllerID))
                            _Locks[_Controller.DeploymentControllerID] = new List<LockOwner>();
            }

            public static bool Lock(BasicFileController controller, string initiationSource)
            {
                LockOwner owner = new LockOwner(controller, initiationSource);

                if (controller.CoordinatedKeyManager.Lock(STEM.Sys.IO.Path.GetFileName(initiationSource), owner, controller.CoordinateWith))
                {
                    lock (_Locks[controller.DeploymentControllerID])
                    {
                        _Locks[controller.DeploymentControllerID].Add(owner);
                        owner._MemberOf = _Locks[controller.DeploymentControllerID];
                    }

                    return true;
                }

                return false;
            }

            public static void Unlock(BasicFileController controller, string initiationSource)
            {
                LockOwner owner = null;

                if (_Locks.ContainsKey(controller.DeploymentControllerID))
                    lock (_Locks[controller.DeploymentControllerID])
                        owner = _Locks[controller.DeploymentControllerID].FirstOrDefault(i => i._InitiationSource.Equals(initiationSource, StringComparison.InvariantCultureIgnoreCase));

                if (owner != null)
                    controller.CoordinatedKeyManager.Unlock(STEM.Sys.IO.Path.GetFileName(initiationSource), owner);
            }

            public static void UnlockAll(string deploymentControllerID)
            {
                List<LockOwner> toDo = new List<LockOwner>();

                if (_Locks.ContainsKey(deploymentControllerID))
                    lock (_Locks)
                        if (_Locks.ContainsKey(deploymentControllerID))
                        {
                            toDo = _Locks[deploymentControllerID];
                            _Locks.Remove(deploymentControllerID);
                        }

                foreach (LockOwner owner in toDo.ToList())
                {
                    try
                    {
                        owner._Controller.CoordinatedKeyManager.Unlock(STEM.Sys.IO.Path.GetFileName(owner._InitiationSource), owner);
                    }
                    catch { }
                }
            }

            public void Locked(string key)
            {
            }

            public void Unlocked(string key)
            {
                try
                {
                    if (_MemberOf != null)
                        lock (_MemberOf)
                            _MemberOf.Remove(this);
                }
                catch { }
            }

            public void Verify(string key)
            {
                if ((DateTime.UtcNow - _LockTime).TotalMinutes > 20)
                    _Controller.CoordinatedKeyManager.Unlock(STEM.Sys.IO.Path.GetFileName(_InitiationSource), this);
            }
        }

        public override void Disposing()
        {
            LockOwner.UnlockAll(DeploymentControllerID);
     
            base.Disposing();
        }
        
        Dictionary<string, Touched> _LastTouched = new Dictionary<string, Touched>();
        List<Used> _LastUsed = new List<Used>();
        
        public override DeploymentDetails GenerateDeploymentDetails(IReadOnlyList<string> listPreprocessResult, string initiationSource, string recommendedBranchIP, IReadOnlyList<string> limitedToBranches)
        {
            DeploymentDetails ret = null;
            try
            {
                if (RequireTargetNameCoordination && CoordinatedKeyManager != null)
                    if (!LockOwner.Lock(this, initiationSource))
                        return null;

                string dp = TemplateKVP.Keys.ToList().FirstOrDefault(i => i.Equals("[DestinationPath]", StringComparison.InvariantCultureIgnoreCase));
                if (dp == null)
                    throw new Exception("No macro [DestinationPath] exists in this DeploymentController.");

                TemplateKVP["[DestinationPath]"] = TemplateKVP[dp];
                
                InstructionSet clone = GetTemplateInstance(true);

                CustomizeInstructionSet(clone, TemplateKVP, recommendedBranchIP, initiationSource, true);

                return new DeploymentDetails(clone, recommendedBranchIP);
            }
            catch (Exception ex)
            {
                STEM.Sys.EventLog.WriteEntry("BasicFileController.GenerateDeploymentDetails", new Exception(InstructionSetTemplate + ": " + initiationSource, ex).ToString(), STEM.Sys.EventLog.EventLogEntryType.Error);
            }
            finally
            {
                if (ret == null)
                {
                    LockOwner.Unlock(this, initiationSource);
                }
            }

            return ret;
        }

        protected bool GoodDirectory(string dir)
        {
            try
            {
                if (CheckDirectoryExists)
                {
                    if (!_LastTouched.ContainsKey(dir))
                        _LastTouched[dir] = new Touched { Exists = false, LastAttempt = DateTime.MinValue };

                    if ((DateTime.UtcNow - _LastTouched[dir].LastAttempt).TotalSeconds > 180 || _LastTouched[dir].Exists)
                    {
                        if (!_LastTouched[dir].Exists)
                            if (!STEM.Sys.IO.Net.PingHost(STEM.Sys.IO.Path.IPFromPath(dir)))
                            {
                                _LastTouched[dir].LastAttempt = DateTime.UtcNow;
                                return false;
                            }

                        if (!String.IsNullOrEmpty(DestinationPathUser) && !String.IsNullOrEmpty(DestinationPathPassword))
                        {
                            if (DirectoryExists(dir, DestinationPathUser, DestinationPathPassword, DestinationPathImpersonationIsLocal))
                            {
                                _LastTouched[dir].Exists = true;
                            }
                            else
                            {
                                _LastTouched[dir].Exists = false;
                            }
                        }
                        else
                        {
                            if (DirectoryExists(dir))
                            {
                                _LastTouched[dir].Exists = true;
                            }
                            else
                            {
                                _LastTouched[dir].Exists = false;
                            }
                        }

                        _LastTouched[dir].LastAttempt = DateTime.UtcNow;
                    }

                    return _LastTouched[dir].Exists;
                }
                else
                    return true;
            }
            catch { }

            return false;
        }
        
        public override void ExecutionComplete(DeploymentDetails details, List<Exception> exceptions)
        {
            LockOwner.Unlock(this, details.InitiationSource);
            base.ExecutionComplete(details, exceptions);
        }
    }
}