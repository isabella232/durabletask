﻿//  ----------------------------------------------------------------------------------
//  Copyright Microsoft Corporation
//  Licensed under the Apache License, Version 2.0 (the "License");
//  you may not use this file except in compliance with the License.
//  You may obtain a copy of the License at
//  http://www.apache.org/licenses/LICENSE-2.0
//  Unless required by applicable law or agreed to in writing, software
//  distributed under the License is distributed on an "AS IS" BASIS,
//  WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
//  See the License for the specific language governing permissions and
//  limitations under the License.
//  ----------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Fabric;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DurableTask;
using DurableTask.ServiceFabric;
using DurableTask.Test.Orchestrations.Perf;
using Microsoft.ServiceFabric.Services.Communication.Runtime;
using Microsoft.ServiceFabric.Services.Remoting.Runtime;
using Microsoft.ServiceFabric.Services.Runtime;
using TestApplication.Common;
using TestStatefulService.TestOrchestrations;

namespace TestStatefulService
{
    /// <summary>
    /// An instance of this class is created for each service replica by the Service Fabric runtime.
    /// </summary>
    internal sealed class TestStatefulService : StatefulService, IRemoteClient
    {
        TaskHubWorker worker;
        TaskHubClient client;
        ReplicaRole currentRole;

        public TestStatefulService(StatefulServiceContext context) : base(context)
        {
            var settings = new FabricOrchestrationProviderSettings();
            settings.TaskOrchestrationDispatcherSettings.DispatcherCount = 5;
            settings.TaskActivityDispatcherSettings.DispatcherCount = 5;

            var fabricProviderFactory = new FabricOrchestrationProviderFactory(this.StateManager, settings);
            this.worker = new TaskHubWorker(fabricProviderFactory.OrchestrationService);
            this.client = new TaskHubClient(fabricProviderFactory.OrchestrationServiceClient);
        }

        /// <summary>
        /// Optional override to create listeners (e.g., HTTP, Service Remoting, WCF, etc.) for this service replica to handle client or user requests.
        /// </summary>
        /// <remarks>
        /// For more information on service communication, see http://aka.ms/servicefabricservicecommunication
        /// </remarks>
        /// <returns>A collection of listeners.</returns>
        protected override IEnumerable<ServiceReplicaListener> CreateServiceReplicaListeners()
        {
            return new[]
            {
                //new ServiceReplicaListener(initParams => new OwinCommunicationListener("TestStatefulService", new Startup(), initParams))
                new ServiceReplicaListener(this.CreateServiceRemotingListener)
            };
        }

        /// <summary>
        /// This is the main entry point for your service replica.
        /// This method executes when this replica of your service becomes primary and has write status.
        /// </summary>
        /// <param name="cancellationToken">Canceled when Service Fabric needs to shut down this service replica.</param>
        protected override async Task RunAsync(CancellationToken cancellationToken)
        {
            await this.worker
                .AddTaskOrchestrations(KnownOrchestrationTypeNames.Values.ToArray())
                .AddTaskActivities(KnownActivities)
                .StartAsync();

            //await this.testExecutor.StartAsync();
        }

        protected override async Task OnChangeRoleAsync(ReplicaRole newRole, CancellationToken cancellationToken)
        {
            if (newRole != ReplicaRole.Primary && this.currentRole == ReplicaRole.Primary)
            {
                //await this.testExecutor.StopAsync();
                await this.worker.StopAsync(isForced: true);
            }
            this.currentRole = newRole;
        }

        public async Task<OrchestrationState> RunOrchestrationAsync(string orchestrationTypeName, object input, TimeSpan waitTimeout)
        {
            var instance = await client.CreateOrchestrationInstanceAsync(GetOrchestrationType(orchestrationTypeName), input);
            return await client.WaitForOrchestrationAsync(instance, waitTimeout);
        }

        public async Task<OrchestrationState> RunDriverOrchestrationAsync(DriverOrchestrationData input, TimeSpan waitTimeout)
        {
            return await this.RunOrchestrationAsync(typeof(DriverOrchestration).Name, input, waitTimeout);
        }

        public Task<OrchestrationInstance> StartTestOrchestrationAsync(TestOrchestrationData input)
        {
            return client.CreateOrchestrationInstanceAsync(typeof(TestOrchestration), input);
        }

        public Task<OrchestrationState> GetOrchestrationState(OrchestrationInstance instance)
        {
            return client.GetOrchestrationStateAsync(instance);
        }

        public async Task<OrchestrationState> WaitForOrchestration(OrchestrationInstance instance, TimeSpan waitTimeout)
        {
            return await client.WaitForOrchestrationAsync(instance, waitTimeout);
        }

        public Task PurgeOrchestrationHistoryEventsAsync()
        {
            return this.client.PurgeOrchestrationInstanceHistoryAsync(DateTime.UtcNow, OrchestrationStateTimeRangeFilterType.OrchestrationCompletedTimeFilter);
        }

        Type GetOrchestrationType(string typeName)
        {
            if (!KnownOrchestrationTypeNames.ContainsKey(typeName))
            {
                throw new Exception($"Unknown Orchestration Type Name : {typeName}");
            }

            return KnownOrchestrationTypeNames.First(kvp => string.Equals(typeName, kvp.Key)).Value;
        }

        public Task<OrchestrationInstance> StartTestOrchestrationWithInstanceIdAsync(string instanceId, TestOrchestrationData input)
        {
            return client.CreateOrchestrationInstanceAsync(typeof(TestOrchestration), instanceId, input);
        }

        public async Task<OrchestrationState> GetOrchestrationStateWithInstanceId(string instanceId)
        {
            var allStates = await this.client.GetOrchestrationStateAsync(instanceId, allExecutions: false);
            return allStates.FirstOrDefault();
        }

        static Dictionary<string, Type> KnownOrchestrationTypeNames = new Dictionary<string, Type>
        {
            { typeof(SimpleOrchestrationWithTasks).Name, typeof(SimpleOrchestrationWithTasks) },
            { typeof(SimpleOrchestrationWithTimer).Name, typeof(SimpleOrchestrationWithTimer) },
            { typeof(GenerationBasicOrchestration).Name, typeof(GenerationBasicOrchestration) },
            { typeof(SimpleOrchestrationWithSubOrchestration).Name, typeof(SimpleOrchestrationWithSubOrchestration) },
            { typeof(DriverOrchestration).Name, typeof(DriverOrchestration) },
            { typeof(TestOrchestration).Name, typeof(TestOrchestration) },
        };

        static Type[] KnownActivities = 
        {
            typeof(GetUserTask),
            typeof(GreetUserTask),
            typeof(GenerationBasicTask),
            typeof(RandomTimeWaitingTask)
        };
    }
}