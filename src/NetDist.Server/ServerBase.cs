﻿using Microsoft.VisualBasic.Devices;
using NetDist.Core;
using NetDist.Core.Utilities;
using NetDist.Jobs.DataContracts;
using NetDist.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace NetDist.Server
{
    /// <summary>
    /// Abstract class for server implementations
    /// </summary>
    public abstract class ServerBase
    {
        /// <summary>
        /// Logger object
        /// </summary>
        public Logger Logger { get; private set; }

        /// <summary>
        /// Dictionary which olds information about the known clients
        /// </summary>
        private readonly ConcurrentDictionary<Guid, ExtendedClientInfo> _knownClients;

        /// <summary>
        /// Abstract method to start the server
        /// </summary>
        protected abstract bool InternalStart();

        /// <summary>
        /// Abstract method to stop the server
        /// </summary>
        protected abstract bool InternalStop();

        /// <summary>
        /// Path to the handlers folder
        /// </summary>
        protected string PackagesFolder { get { return Path.Combine(Path.GetDirectoryName(new Uri(Assembly.GetExecutingAssembly().CodeBase).LocalPath), "packages"); } }

        /// <summary>
        /// Settings object
        /// </summary>
        private IServerSettings _settings;

        private readonly PackageManager _packageManager;
        private readonly HandlerManager _handlerManager;

        /// <summary>
        /// Constructor
        /// </summary>
        protected ServerBase()
        {
            Logger = new Logger();
            _knownClients = new ConcurrentDictionary<Guid, ExtendedClientInfo>();
            _packageManager = new PackageManager(PackagesFolder);
            _handlerManager = new HandlerManager(Logger, _packageManager);
            // Make sure the packages folder exists
            Directory.CreateDirectory(PackagesFolder);
        }

        /// <summary>
        /// Initialize with the given settings
        /// </summary>
        protected void InitializeSettings(IServerSettings settings)
        {
            _settings = settings;
            // Autostart if wanted
            if (_settings.AutoStart)
            {
                Start();
            }
        }

        /// <summary>
        /// Starts the server
        /// </summary>
        public void Start()
        {
            var success = InternalStart();
            if (!success)
            {
                Logger.Info("Failed to start");
            }
        }

        /// <summary>
        /// Stops the server and all handlers
        /// </summary>
        public void Stop()
        {
            var success = InternalStop();
            if (!success)
            {
                Logger.Info("Failed to stop");
            }
            _handlerManager.TearDown();
        }

        /// <summary>
        /// Get statistics about the server and the job handlers
        /// </summary>
        public ServerInfo GetStatistics()
        {
            var info = new ServerInfo();
            // RAM information
            var ci = new ComputerInfo();
            info.TotalMemory = ci.TotalPhysicalMemory;
            info.UsedMemory = ci.TotalPhysicalMemory - ci.AvailablePhysicalMemory;
            // CPU information
            info.CpuUsage = CpuUsageReader.GetValue();
            // Handler information
            var handlerStats = _handlerManager.GetStatistics();
            info.Handlers.AddRange(handlerStats);
            // Client information
            foreach (var kvp in _knownClients)
            {
                var clientInfo = kvp.Value;
                info.Clients.Add(clientInfo);
            }
            return info;
        }

        /// <summary>
        /// Get information about currently registered packages
        /// </summary>
        public List<PackageInfo> GetRegisteredPackages()
        {
            var info = new List<PackageInfo>();
            foreach (var file in new DirectoryInfo(PackagesFolder).EnumerateFiles())
            {
                info.Add(_packageManager.GetInfo(Path.GetFileNameWithoutExtension(file.FullName)));
            }
            return info;
        }

        /// <summary>
        /// Register a package (handlers, dependencies, ...)
        /// </summary>
        public bool RegisterPackage(PackageInfo packageInfo, byte[] zipcontent)
        {
            // Save package information
            _packageManager.Save(packageInfo);
            // Unpack the zip file
            ZipUtility.ZipExtractToDirectory(zipcontent, PackagesFolder, true);
            Logger.Info("Registered package '{0}' with {1} handler file(s) and {2} dependent file(s)", packageInfo.PackageName, packageInfo.HandlerAssemblies.Count, packageInfo.Dependencies.Count);
            return true;
        }

        /// <summary>
        /// Called when a new job script is added
        /// Initializes and starts the appropriate handler
        /// </summary>
        public AddJobScriptResult AddJobScript(JobScriptInfo jobScriptInfo)
        {
            return _handlerManager.Add(jobScriptInfo);
        }

        /// <summary>
        /// Stops and removes a job handler
        /// </summary>
        public bool RemoveJobScript(Guid handlerId)
        {
            return _handlerManager.Remove(handlerId);
        }

        /// <summary>
        /// Starts the handler so jobs are being distributed
        /// </summary>
        public bool StartJobScript(Guid handlerId)
        {
            return _handlerManager.Start(handlerId);
        }

        /// <summary>
        /// Stops a handler so no more jobs are distributed and processed
        /// </summary>
        public bool StopJobScript(Guid handlerId)
        {
            return _handlerManager.Stop(handlerId);
        }

        public bool PauseJobScript(Guid handlerId)
        {
            return _handlerManager.Pause(handlerId);
        }

        public bool DisableJobScript(Guid handlerId)
        {
            return _handlerManager.Disable(handlerId);
        }

        public bool EnableJobScript(Guid handlerId)
        {
            return _handlerManager.Enable(handlerId);
        }

        /// <summary>
        /// Get a job from the current pending jobs in the handlers
        /// </summary>
        public Job GetJob(Guid clientId)
        {
            var clientInfo = _knownClients[clientId];
            Logger.Info(entry => entry.SetClientId(clientId), "'{0}' requested a job", clientInfo.ClientInfo.Name);
            var job = _handlerManager.GetJob(clientInfo);
            if (job != null)
            {
                // Update statistics
                _knownClients[clientId].JobsInProgress++;
            }
            return job;
        }

        /// <summary>
        /// Get information for the client for the given handler to execute the job
        /// </summary>
        public HandlerJobInfo GetHandlerJobInfo(Guid handlerId)
        {
            return _handlerManager.GetHandlerJobInfo(handlerId);
        }

        /// <summary>
        /// Gets a file from the package of the specified handler
        /// </summary>
        public byte[] GetFile(Guid handlerId, string file)
        {
            var handlerPackageName = _handlerManager.GetPackageName(handlerId);
            if (handlerPackageName != null)
            {
                var fileContent = _packageManager.GetFile(handlerPackageName, file);
                return fileContent;
            }
            return null;
        }

        /// <summary>
        /// Received a result for one of the jobs from a client
        /// </summary>
        public void ReceiveResult(JobResult result)
        {
            var success = _handlerManager.ProcessResult(result);
            // Update statistics
            _knownClients[result.ClientId].JobsInProgress--;
            if (success)
            {
                _knownClients[result.ClientId].TotalJobsProcessed++;
            }
            else
            {
                _knownClients[result.ClientId].TotalJobsFailed++;
            }
        }

        /// <summary>
        /// Receive information from the client about the client
        /// </summary>
        public void ReceivedClientInfo(ClientInfo info)
        {
            _knownClients.AddOrUpdate(info.Id, guid => new ExtendedClientInfo
            {
                ClientInfo = info,
                LastCommunicationDate = DateTime.Now
            }, (guid, wrapper) =>
            {
                wrapper.ClientInfo = info;
                wrapper.LastCommunicationDate = DateTime.Now;
                return wrapper;
            });
        }
    }
}
