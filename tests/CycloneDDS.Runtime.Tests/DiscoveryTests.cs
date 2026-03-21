using System;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using CycloneDDS.Runtime;
using CycloneDDS.Core;
using CycloneDDS.Runtime.Interop;

namespace CycloneDDS.Runtime.Tests
{
    public class DiscoveryTests : IDisposable
    {
        private static readonly TimeSpan DiscoveryTimeout = TimeSpan.FromSeconds(5);

        private DdsParticipant _participant;
        private string _topicName;

        public DiscoveryTests()
        {
            _participant = new DdsParticipant();
            _topicName = "DiscTest_" + Guid.NewGuid();
        }

        public void Dispose()
        {
            _participant?.Dispose();
        }

        [Fact]
        public async Task PublicationMatched_EventFires_OnReaderCreation()
        {
             using var writer = new DdsWriter<TestMessage>(_participant, _topicName);
             var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
             
             writer.PublicationMatched += (s, e) => {
                 if (e.CurrentCount > 0) tcs.TrySetResult(true);
             };
             
             using var reader = new DdsReader<TestMessage>(_participant, _topicName);
             
             var task = await Task.WhenAny(tcs.Task, Task.Delay(DiscoveryTimeout));
             Assert.Equal(tcs.Task, task);
             Assert.True(tcs.Task.Result);
             
             Assert.Equal(1u, writer.CurrentStatus.CurrentCount);
        }

        [Fact]
        public async Task WaitForReaderAsync_CompletesOnDiscovery()
        {
             using var writer = new DdsWriter<TestMessage>(_participant, _topicName);
             
             var waitTask = writer.WaitForReaderAsync(DiscoveryTimeout);
             
             using var reader = new DdsReader<TestMessage>(_participant, _topicName);
             
             var completed = await Task.WhenAny(waitTask, Task.Delay(DiscoveryTimeout));
             Assert.Same(waitTask, completed);
             Assert.True(waitTask.Result);
        }

        [Fact]
        public void PublicationMatched_EventFires_OnReaderDispose()
        {
             using var writer = new DdsWriter<TestMessage>(_participant, _topicName);
             using var reader = new DdsReader<TestMessage>(_participant, _topicName);
             
             // Wait for discovery
             Assert.True(writer.WaitForReaderAsync(DiscoveryTimeout).Result);
             
             // Setup disconnect monitoring
             var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
             writer.PublicationMatched += (s, e) => {
                 if (e.CurrentCountChange < 0) tcs.TrySetResult(true);
             };
             
             reader.Dispose();
             
             // Wait for disconnect event
             Assert.True(tcs.Task.Wait(DiscoveryTimeout));
        }

        [Fact]
        public void SubscriptionMatched_CurrentCount_Accurate()
        {
             using var reader = new DdsReader<TestMessage>(_participant, _topicName);
             Assert.Equal(0u, reader.CurrentStatus.CurrentCount);
             
             // Writer 1
             using var w1 = new DdsWriter<TestMessage>(_participant, _topicName);
             Thread.Sleep(1000); // Wait discovery
             Assert.Equal(1u, reader.CurrentStatus.CurrentCount);
             
             // Writer 2
             using (var w2 = new DdsWriter<TestMessage>(_participant, _topicName))
             {
                 Thread.Sleep(1000);
                 Assert.Equal(2u, reader.CurrentStatus.CurrentCount);
             } 
             // w2 disposed
             Thread.Sleep(1000);
             Assert.Equal(1u, reader.CurrentStatus.CurrentCount);
        }
        
        [Fact]
        public async Task WaitForReaderAsync_Timeout_ReturnsFalse()
        {
             using var writer = new DdsWriter<TestMessage>(_participant, _topicName);
             var result = await writer.WaitForReaderAsync(TimeSpan.FromMilliseconds(500));
             Assert.False(result);
        }
    }
}
