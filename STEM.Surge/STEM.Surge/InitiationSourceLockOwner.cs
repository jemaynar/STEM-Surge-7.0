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
using System.Linq;
using STEM.Sys.State;
using STEM.Sys.Threading;

namespace STEM.Surge
{
    /// <summary>
    /// Only used inside the SurgeDeploymentManager
    /// </summary>
    public class InitiationSourceLockOwner : IKeyLockOwner
    {
        public string InitiationSource { get; set; }
        public Guid InstructionSetID { get; set; }
        public DeploymentDetails DeploymentDetails { get; set; }
        public _BranchEntry Branch { get; set; }
        public string SwitchboardRowID { get; private set; }
        public string DeploymentControllerID { get { return ControllerManager.DeploymentControllerID; } }

        public bool ExecutionCompleted { get; private set; }

        DateTime LockTime = DateTime.MinValue;

        public _ControllerManager ControllerManager { get; private set; }

        public InitiationSourceLockOwner(_ControllerManager controllerManager)
        {
            InitiationSource = null;
            InstructionSetID = Guid.Empty;
            ExecutionCompleted = false;
            ControllerManager = controllerManager;
            SwitchboardRowID = ControllerManager.SwitchboardRowID;
        }

        public override string ToString()
        {
            return "Branch: " + (Branch != null ? Branch.BranchIP : "Unassigned") + " - " + ControllerManager.DeploymentControllerDescription;
        }

        public bool Lock(string initiationSource)
        {
            lock (this)
            {
                if (initiationSource != null)
                {
                    string key = initiationSource;
                    _FileDeploymentController fileBasis = ControllerManager.ValidatedController as _FileDeploymentController;
                    if (fileBasis != null)
                        if (fileBasis.RequireTargetNameCoordination)
                            key = STEM.Sys.IO.Path.GetFileName(initiationSource);

                    if (ControllerManager.KeyManager.Lock(key, this, ControllerManager.CoordinateWith))
                    {
                        LockTime = DateTime.UtcNow;
                        InitiationSource = initiationSource;
                        return true;
                    }
                }

                return false;
            }
        }

        public void Unlock()
        {
            lock (this)
            {
                try
                {
                    if (ExecutionCompleted)
                        return;

                    if (InitiationSource != null && InstructionSetID != Guid.Empty)
                    {
                        Messages.ExecutionCompleted ec = new Messages.ExecutionCompleted { DeploymentControllerID = ControllerManager.DeploymentControllerID, InstructionSetID = this.InstructionSetID, InitiationSource = this.InitiationSource, Exceptions = new List<Exception>(), InstructionSetXml = null };
                        Unlock(ec);
                    }
                    else
                    {
                        Unlock(null);
                    }
                }
                catch (Exception ex)
                {
                    STEM.Sys.EventLog.WriteEntry("InitiationSourceLockOwner.Unlock", ex.ToString(), STEM.Sys.EventLog.EventLogEntryType.Error);
                }
            }
        }

        public void Unlock(STEM.Surge.Messages.ExecutionCompleted message)
        {
            lock (this)
            {
                if (ExecutionCompleted)
                    return;

                if (Branch != null)
                    try
                    {
                        Branch.Remove(this);
                    }
                    catch { }

                if (DeploymentDetails != null && message != null)
                {
                    try
                    {
                        if (DeploymentDetails.Completed == DateTime.MinValue || message.TimeCompleted > DateTime.MinValue)
                        {
                            DeploymentDetails.Exceptions = message.Exceptions;

                            DeploymentDetails.Completed = message.TimeCompleted;

                            if (DeploymentDetails.Completed == DateTime.MinValue)
                                DeploymentDetails.Completed = DateTime.UtcNow;

                            if (DeploymentDetails.Issued == DateTime.MinValue || DeploymentDetails.Issued > DeploymentDetails.Completed)
                                DeploymentDetails.Issued = DeploymentDetails.Completed;

                            if (DeploymentDetails.Received == DateTime.MinValue || DeploymentDetails.Received > DeploymentDetails.Completed)
                                DeploymentDetails.Received = DeploymentDetails.Completed;

                            DeploymentDetails.LastModified = DateTime.UtcNow;

                            if (message.InstructionSetXml != null)
                            {
                                try
                                {
                                    _InstructionSet iSet = _InstructionSet.Deserialize(message.InstructionSetXml) as _InstructionSet;

                                    if (iSet != null)
                                    {
                                        DeploymentDetails.ISet = iSet;
                                    }
                                    else if (DeploymentDetails.ISet != null)
                                    {
                                        DeploymentDetails.ISet.Assigned = DeploymentDetails.Issued;
                                        DeploymentDetails.ISet.Started = DeploymentDetails.Received;
                                        DeploymentDetails.ISet.Received = DeploymentDetails.Received;
                                        DeploymentDetails.ISet.Completed = DeploymentDetails.Completed;
                                    }
                                }
                                catch { }
                            }
                            else if (DeploymentDetails.ISet != null)
                            {
                                DeploymentDetails.ISet.Assigned = DeploymentDetails.Issued;
                                DeploymentDetails.ISet.Started = DeploymentDetails.Received;
                                DeploymentDetails.ISet.Received = DeploymentDetails.Received;
                                DeploymentDetails.ISet.Completed = DeploymentDetails.Completed;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        STEM.Sys.EventLog.WriteEntry("InitiationSourceLockOwner.Unlock", ex.ToString(), STEM.Sys.EventLog.EventLogEntryType.Error);
                    }
                }

                try
                {
                    if (message != null)
                        try
                        {
                            _ExecutionCompletePool.BeginAsync(new ExecutionComplete(ControllerManager, DeploymentDetails, message), TimeSpan.FromMilliseconds(100));
                            //if (ControllerManager.CurrentPhase != _ControllerManager.ExecutionPhase.Disposing)
                            //{
                            //    DateTime n = DateTime.UtcNow;
                            //    DateTime e = DateTime.UtcNow;

                            //    _DeploymentController controller = ControllerManager.ValidatedController;

                            //    if (controller != null)
                            //        try
                            //        {
                            //            if (DeploymentDetails != null)
                            //            {
                            //                controller.ExecutionComplete(DeploymentDetails, message.Exceptions);
                            //                controller.InstructionMessageReceived(message, DeploymentDetails);
                            //            }

                            //            controller.MessageReceived(message);

                            //            e = DateTime.UtcNow;
                            //            if ((e - n).TotalMilliseconds > 1000)
                            //                STEM.Sys.EventLog.WriteEntry("InitiationSourceLockOwner.InstructionMessageReceived", "Direct Call \r\nController: " + ControllerManager.DeploymentController + " - Time: " + (e - n).TotalMilliseconds + " ms", STEM.Sys.EventLog.EventLogEntryType.Error);
                            //        }
                            //        catch (Exception ex)
                            //        {
                            //            STEM.Sys.EventLog.WriteEntry("InitiationSourceLockOwner.Unlock", new Exception("Call into " + ControllerManager.DeploymentController + ".ExecutionComplete() threw an exception.", ex), STEM.Sys.EventLog.EventLogEntryType.Error);
                            //        }
                            //}
                        }
                        catch (Exception ex)
                        {
                            STEM.Sys.EventLog.WriteEntry("InitiationSourceLockOwner.Unlock", ex, STEM.Sys.EventLog.EventLogEntryType.Error);
                        }

                    if (InitiationSource != null)
                        try
                        {
                            string key = InitiationSource;
                            _FileDeploymentController fileBasis = ControllerManager.ValidatedController as _FileDeploymentController;
                            if (fileBasis != null)
                                if (fileBasis.RequireTargetNameCoordination)
                                    key = STEM.Sys.IO.Path.GetFileName(InitiationSource);

                            ControllerManager.KeyManager.Unlock(key, this);
                        }
                        catch (Exception ex)
                        {
                            STEM.Sys.EventLog.WriteEntry("InitiationSourceLockOwner.Unlock", ex.ToString(), STEM.Sys.EventLog.EventLogEntryType.Error);
                        }
                        finally
                        {
                            InitiationSource = null;
                        }
                }
                catch (Exception ex)
                {
                    STEM.Sys.EventLog.WriteEntry("InitiationSourceLockOwner.Unlock", new Exception(ControllerManager.DeploymentController, ex), STEM.Sys.EventLog.EventLogEntryType.Error);
                }
                finally
                {
                    ExecutionCompleted = true;
                }
            }
        }

        static STEM.Sys.Threading.ThreadPool _ExecutionCompletePool = new Sys.Threading.ThreadPool(Int32.MaxValue, true);

        class ExecutionComplete : STEM.Sys.Threading.IThreadable
        {
            _ControllerManager _ControllerManager;
            DeploymentDetails _DeploymentDetails;
            STEM.Surge.Messages.ExecutionCompleted _Message;

            public ExecutionComplete(_ControllerManager controllerManager, DeploymentDetails deploymentDetails, STEM.Surge.Messages.ExecutionCompleted msg)
            {
                _ControllerManager = controllerManager;
                _DeploymentDetails = deploymentDetails;
                _Message = msg;
            }

            protected override void Execute(ThreadPool owner)
            {
                try
                {
                    if (_Message != null && _ControllerManager.CurrentPhase != _ControllerManager.ExecutionPhase.Disposing)
                    {
                        DateTime n = DateTime.UtcNow;
                        DateTime e = DateTime.UtcNow;

                        _DeploymentController controller = _ControllerManager.ValidatedController;

                        if (controller == null)
                            throw new Exception("ValidatedController is null.");

                        try
                        {
                            if (_DeploymentDetails != null)
                            {
                                controller.ExecutionComplete(_DeploymentDetails, _Message.Exceptions);
                                controller.InstructionMessageReceived(_Message, _DeploymentDetails);
                            }

                            controller.MessageReceived(_Message);

                            e = DateTime.UtcNow;
                            if ((e - n).TotalMilliseconds > 1000)
                                STEM.Sys.EventLog.WriteEntry("InitiationSourceLockOwner.InstructionMessageReceived", "Direct Call \r\nController: " + _ControllerManager.DeploymentController + " - Time: " + (e - n).TotalMilliseconds + " ms", STEM.Sys.EventLog.EventLogEntryType.Error);
                        }
                        catch (Exception ex)
                        {
                            STEM.Sys.EventLog.WriteEntry("InitiationSourceLockOwner.Unlock", new Exception("Call into " + _ControllerManager.DeploymentController + ".ExecutionComplete() threw an exception.", ex), STEM.Sys.EventLog.EventLogEntryType.Error);
                        }

                        _Message = null;
                    }

                    owner.EndAsync(this);
                }
                catch { }
            }
        }


        public void Locked(string key)
        {
        }

        public void Unlocked(string key)
        {
        }

        public void Verify(string key)
        {
            if (InitiationSource == null)
                InitiationSource = key;

            if (DeploymentDetails != null && DeploymentDetails.Completed != DateTime.MinValue)
            {
                if (ExecutionCompleted)
                {
                    try
                    {
                        ControllerManager.KeyManager.Unlock(key, this);
                    }
                    catch (Exception ex)
                    {
                        STEM.Sys.EventLog.WriteEntry("InitiationSourceLockOwner.Unlock", ex.ToString(), STEM.Sys.EventLog.EventLogEntryType.Error);
                    }
                    finally
                    {
                        InitiationSource = null;
                    }
                }
                else
                {
                    Unlock();
                }
            }

            if ((DateTime.UtcNow - LockTime).TotalMinutes > 10 && !ExecutionCompleted)
            {
                if (DeploymentDetails == null)
                {
                    STEM.Sys.EventLog.WriteEntry("InitiationSourceLockOwner.Verify", "NULL Unlock 10min - Lock key: " + key + ", InitiationSource: " + InitiationSource, STEM.Sys.EventLog.EventLogEntryType.Information);

                    Unlock();
                }
                else if (DeploymentDetails.Received == DateTime.MinValue || DeploymentDetails.Completed != DateTime.MinValue)
                {
                    if (DeploymentDetails.Completed != DateTime.MinValue)
                        STEM.Sys.EventLog.WriteEntry("InitiationSourceLockOwner.Verify", "Non NULL Unlock (Completed - 10min) - Branch: " + DeploymentDetails.BranchIP + ", Lock key: " + key + ", InitiationSource: " + InitiationSource, STEM.Sys.EventLog.EventLogEntryType.Information);
                    else
                        STEM.Sys.EventLog.WriteEntry("InitiationSourceLockOwner.Verify", "Non NULL Unlock (Not completed - 10min) - Branch: " + DeploymentDetails.BranchIP + ", Lock key: " + key + ", InitiationSource: " + InitiationSource, STEM.Sys.EventLog.EventLogEntryType.Information);

                    Unlock();
                }
            }
            else if ((DateTime.UtcNow - LockTime).TotalMinutes > 2 && !ExecutionCompleted)
            {
                if (DeploymentDetails == null)
                {
                    STEM.Sys.EventLog.WriteEntry("InitiationSourceLockOwner.Verify", "NULL Unlock 2min - Lock key: " + key + ", InitiationSource: " + InitiationSource, STEM.Sys.EventLog.EventLogEntryType.Information);

                    Unlock();
                }
                else
                {
                    if (DeploymentDetails.Completed != DateTime.MinValue)
                    {
                        STEM.Sys.EventLog.WriteEntry("InitiationSourceLockOwner.Verify", "Non NULL Unlock (Completed - 2min) - Branch: " + DeploymentDetails.BranchIP + ", Lock key: " + key + ", InitiationSource: " + InitiationSource, STEM.Sys.EventLog.EventLogEntryType.Information);

                        Unlock();
                    }
                }
            }
            else if ((DateTime.UtcNow - LockTime).TotalMinutes > 1)
            {
                if (Branch == null)
                {
                    if (ExecutionCompleted)
                    {
                        if (InitiationSource != null)
                            try
                            {
                                ControllerManager.KeyManager.Unlock(key, this);
                            }
                            catch (Exception ex)
                            {
                                STEM.Sys.EventLog.WriteEntry("InitiationSourceLockOwner.Unlock", ex.ToString(), STEM.Sys.EventLog.EventLogEntryType.Error);
                            }
                            finally
                            {
                                InitiationSource = null;
                            }
                    }
                    else
                    {
                        Unlock();
                    }
                }
            }
        }
    }
}
