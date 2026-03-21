using System;
using Xunit;
using CycloneDDS.Runtime;
using CycloneDDS.Runtime.Tests;

namespace CycloneDDS.Runtime.Tests
{
    public class DdsWriterTests
    {
        private static string NewTopicName() => $"WriterTest_{Guid.NewGuid():N}";

        [Fact]
        public void CreateWriter_Success()
        {
            using var participant = new DdsParticipant(0);
            
            using var writer = new DdsWriter<TestMessage>(participant, NewTopicName());
        }

        [Fact]
        public void Write_SingleSample_Success()
        {
            using var participant = new DdsParticipant(0);
            using var writer = new DdsWriter<TestMessage>(participant, NewTopicName());
            
            var data = new TestMessage { Id = 1, Value = 123 };
            writer.Write(data);
        }
        
        [Fact]
        public void Dispose_Idempotent()
        {
            using var participant = new DdsParticipant(0);
            var writer = new DdsWriter<TestMessage>(participant, NewTopicName());
            
            writer.Dispose();
            writer.Dispose();
        }

        [Fact]
        public void Write_AfterDispose_Throws()
        {
            using var participant = new DdsParticipant(0);
            var writer = new DdsWriter<TestMessage>(participant, NewTopicName());
            
            writer.Dispose();
            
            Assert.Throws<ObjectDisposedException>(() => writer.Write(new TestMessage()));
        }
    }
}
