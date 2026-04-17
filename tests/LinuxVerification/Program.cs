using System;
using System.Threading;
using CycloneDDS.Runtime;
using CycloneDDS.Runtime.Tests;

namespace LinuxVerification
{
    class Program
    {
        private static int _passed = 0;
        private static int _failed = 0;
        private static readonly object _lock = new();
        private static int _topicCounter = 0;

        static void Main(string[] args)
        {
            Console.WriteLine("╔══════════════════════════════════════════════════════════════╗");
            Console.WriteLine("║     CycloneDDS.NET - Comprehensive Linux Verification       ║");
            Console.WriteLine("╚══════════════════════════════════════════════════════════════╝");
            Console.WriteLine();

            // Run all tests
            Test_Participant_CreateAndDispose();
            Test_Participant_DefaultDomain();
            Test_Participant_CustomDomain();

            Test_Writer_Create();
            Test_Writer_PublishTestMessage();

            Test_Reader_Create();
            Test_Reader_TakeData();
            Test_Reader_ReadData();

            Test_PubSub_FullRoundtrip();
            Test_PubSub_MultipleWriters();
            Test_PubSub_MultipleReaders();

            Test_Serialization_Size();
            Test_Serialization_AllTypes();

            Test_Write_1000Samples();

            // Test_Instance_Dispose - skipped (requires native key delegates)

            // Print summary
            Console.WriteLine();
            Console.WriteLine("╔══════════════════════════════════════════════════════════════╗");
            Console.WriteLine("║                      TEST SUMMARY                            ║");
            Console.WriteLine("╚══════════════════════════════════════════════════════════════╝");
            Console.WriteLine($"  ✓ Passed: {_passed}");
            Console.WriteLine($"  ✗ Failed: {_failed}");
            Console.WriteLine($"  Total:    {_passed + _failed}");
            Console.WriteLine();

            if (_failed > 0)
            {
                Console.WriteLine("SOME TESTS FAILED!");
                Environment.Exit(1);
            }
            else
            {
                Console.WriteLine("ALL TESTS PASSED!");
                Environment.Exit(0);
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // PARTICIPANT TESTS
        // ═══════════════════════════════════════════════════════════════

        static void Test_Participant_CreateAndDispose()
        {
            try
            {
                using var participant = new DdsParticipant(0);
                Assert(!participant.IsDisposed, "Participant should not be disposed");
                Log("✓ Test_Participant_CreateAndDispose PASSED");
            }
            catch (Exception ex)
            {
                Fail($"Test_Participant_CreateAndDispose: {ex.Message}");
            }
        }

        static void Test_Participant_DefaultDomain()
        {
            try
            {
                using var participant = new DdsParticipant();
                Assert(!participant.IsDisposed, "Participant should not be disposed");
                Assert(participant.DomainId == 0, "Default domain should be 0");
                Log("✓ Test_Participant_DefaultDomain PASSED");
            }
            catch (Exception ex)
            {
                Fail($"Test_Participant_DefaultDomain: {ex.Message}");
            }
        }

        static void Test_Participant_CustomDomain()
        {
            try
            {
                using var participant = new DdsParticipant(42);
                Assert(!participant.IsDisposed, "Participant should not be disposed");
                Assert(participant.DomainId == 42, "Domain should be 42");
                Log("✓ Test_Participant_CustomDomain PASSED");
            }
            catch (Exception ex)
            {
                Fail($"Test_Participant_CustomDomain: {ex.Message}");
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // WRITER TESTS
        // ═══════════════════════════════════════════════════════════════

        static void Test_Writer_Create()
        {
            try
            {
                using var participant = new DdsParticipant(0);
                string topic = GetUniqueTopic();
                using var writer = new DdsWriter<TestMessage>(participant, topic);
                // Writer created successfully
                Log("✓ Test_Writer_Create PASSED");
            }
            catch (Exception ex)
            {
                Fail($"Test_Writer_Create: {ex.Message}");
            }
        }

        static void Test_Writer_PublishTestMessage()
        {
            try
            {
                using var participant = new DdsParticipant(0);
                string topic = GetUniqueTopic();
                using var writer = new DdsWriter<TestMessage>(participant, topic);

                var message = new TestMessage { Id = 123, Value = 456 };
                writer.Write(message);

                Thread.Sleep(100);
                Log("✓ Test_Writer_PublishTestMessage PASSED");
            }
            catch (Exception ex)
            {
                Fail($"Test_Writer_PublishTestMessage: {ex.Message}");
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // READER TESTS
        // ═══════════════════════════════════════════════════════════════

        static void Test_Reader_Create()
        {
            try
            {
                using var participant = new DdsParticipant(0);
                string topic = GetUniqueTopic();
                using var reader = new DdsReader<TestMessage>(participant, topic);
                // Reader created successfully
                Log("✓ Test_Reader_Create PASSED");
            }
            catch (Exception ex)
            {
                Fail($"Test_Reader_Create: {ex.Message}");
            }
        }

        static void Test_Reader_TakeData()
        {
            try
            {
                using var participant = new DdsParticipant(0);
                string topic = GetUniqueTopic();
                using var writer = new DdsWriter<TestMessage>(participant, topic);
                using var reader = new DdsReader<TestMessage>(participant, topic);

                // Write some data
                var message = new TestMessage { Id = 456, Value = 789 };
                writer.Write(message);

                // Wait for discovery and data
                Thread.Sleep(500);

                // Take data from reader
                using var samples = reader.Take();
                Assert(samples.Count > 0, "Should receive at least one sample");

                bool found = false;
                for (int i = 0; i < samples.Count; i++)
                {
                    if (samples.Infos[i].ValidData != 0)
                    {
                        Assert(samples[i].Id == 456, $"Expected Id=456, got {samples[i].Id}");
                        Assert(samples[i].Value == 789, $"Expected Value=789, got {samples[i].Value}");
                        found = true;
                        break;
                    }
                }
                Assert(found, "Should have found valid data");

                Log("✓ Test_Reader_TakeData PASSED");
            }
            catch (Exception ex)
            {
                Fail($"Test_Reader_TakeData: {ex.Message}");
            }
        }

        static void Test_Reader_ReadData()
        {
            try
            {
                using var participant = new DdsParticipant(0);
                string topic = GetUniqueTopic();
                using var writer = new DdsWriter<TestMessage>(participant, topic);
                using var reader = new DdsReader<TestMessage>(participant, topic);

                // Write data
                var message = new TestMessage { Id = 999, Value = 888 };
                writer.Write(message);

                Thread.Sleep(500);

                // Read without removing
                using var samples = reader.Read();
                Assert(samples.Count > 0, "Should have samples available");

                // Read again - should still have data (not removed)
                using var samples2 = reader.Read();
                Assert(samples2.Count > 0, "Should still have samples after Read");

                Log("✓ Test_Reader_ReadData PASSED");
            }
            catch (Exception ex)
            {
                Fail($"Test_Reader_ReadData: {ex.Message}");
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // PUB/SUB COMMUNICATION TESTS
        // ═══════════════════════════════════════════════════════════════

        static void Test_PubSub_FullRoundtrip()
        {
            try
            {
                using var participant = new DdsParticipant(0);
                string topic = GetUniqueTopic();
                using var writer = new DdsWriter<TestMessage>(participant, topic);
                using var reader = new DdsReader<TestMessage>(participant, topic);

                // Write sample
                var sent = new TestMessage { Id = 42, Value = 123456 };
                writer.Write(sent);

                // Wait for delivery
                Thread.Sleep(500);

                // Read sample
                using var scope = reader.Take();

                Assert(scope.Count > 0, "Should have received at least one sample");

                bool found = false;
                for (int i = 0; i < scope.Count; i++)
                {
                    if (scope.Infos[i].ValidData != 0)
                    {
                        AssertEqual(42, scope[i].Id);
                        AssertEqual(123456, scope[i].Value);
                        found = true;
                        break;
                    }
                }
                Assert(found, "Should have received valid data");

                Log("✓ Test_PubSub_FullRoundtrip PASSED");
            }
            catch (Exception ex)
            {
                Fail($"Test_PubSub_FullRoundtrip: {ex.Message}");
            }
        }

        static void Test_PubSub_MultipleWriters()
        {
            try
            {
                using var participant = new DdsParticipant(0);
                string topic = GetUniqueTopic();
                using var writer1 = new DdsWriter<TestMessage>(participant, topic);
                using var writer2 = new DdsWriter<TestMessage>(participant, topic);
                using var reader = new DdsReader<TestMessage>(participant, topic);

                // Both writers publish
                writer1.Write(new TestMessage { Id = 1, Value = 100 });
                writer2.Write(new TestMessage { Id = 2, Value = 200 });

                Thread.Sleep(1000);

                using var samples = reader.Take();
                // May get 1 or 2 samples depending on timing
                Assert(samples.Count >= 1, $"Expected at least 1 sample, got {samples.Count}");

                Log("✓ Test_PubSub_MultipleWriters PASSED");
            }
            catch (Exception ex)
            {
                Fail($"Test_PubSub_MultipleWriters: {ex.Message}");
            }
        }

        static void Test_PubSub_MultipleReaders()
        {
            try
            {
                using var participant = new DdsParticipant(0);
                string topic = GetUniqueTopic();
                using var writer = new DdsWriter<TestMessage>(participant, topic);
                using var reader1 = new DdsReader<TestMessage>(participant, topic);
                using var reader2 = new DdsReader<TestMessage>(participant, topic);

                // Writer publishes
                writer.Write(new TestMessage { Id = 999, Value = 777 });

                Thread.Sleep(500);

                // Both readers should receive
                using var samples1 = reader1.Take();
                using var samples2 = reader2.Take();

                Assert(samples1.Count > 0, "Reader1 should receive data");
                Assert(samples2.Count > 0, "Reader2 should receive data");

                Log("✓ Test_PubSub_MultipleReaders PASSED");
            }
            catch (Exception ex)
            {
                Fail($"Test_PubSub_MultipleReaders: {ex.Message}");
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // SERIALIZATION TESTS
        // ═══════════════════════════════════════════════════════════════

        static void Test_Serialization_Size()
        {
            try
            {
                int size = System.Runtime.InteropServices.Marshal.SizeOf<TestMessage>();
                Assert(size > 0, "Should calculate size");
                Log($"  → TestMessage serialized size: {size} bytes");
                Log("✓ Test_Serialization_Size PASSED");
            }
            catch (Exception ex)
            {
                Fail($"Test_Serialization_Size: {ex.Message}");
            }
        }

        static void Test_Serialization_AllTypes()
        {
            try
            {
                // Test with KeyedTestMessage which has more fields
                int size = System.Runtime.InteropServices.Marshal.SizeOf<KeyedTestMessage>();
                Assert(size > 0, "Should calculate size");
                Log($"  → KeyedTestMessage serialized size: {size} bytes");
                Log("✓ Test_Serialization_AllTypes PASSED");
            }
            catch (Exception ex)
            {
                Fail($"Test_Serialization_AllTypes: {ex.Message}");
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // PERFORMANCE TESTS
        // ═══════════════════════════════════════════════════════════════

        static void Test_Write_1000Samples()
        {
            try
            {
                using var participant = new DdsParticipant(0);
                string topic = GetUniqueTopic();
                using var writer = new DdsWriter<TestMessage>(participant, topic);

                var msg = new TestMessage { Id = 1, Value = 123 };

                long startAlloc = GC.GetTotalAllocatedBytes(true);

                for (int i = 0; i < 1000; i++)
                {
                    writer.Write(msg);
                }

                long endAlloc = GC.GetTotalAllocatedBytes(true);
                long diff = endAlloc - startAlloc;

                Console.WriteLine($"  → 1000 writes allocated: {diff} bytes");
                Log("✓ Test_Write_1000Samples PASSED");
            }
            catch (Exception ex)
            {
                Fail($"Test_Write_1000Samples: {ex.Message}");
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // INSTANCE MANAGEMENT TESTS
        // ═══════════════════════════════════════════════════════════════

        static void Test_Instance_Dispose()
        {
            try
            {
                using var participant = new DdsParticipant(0);
                string topic = GetUniqueTopic();
                using var writer = new DdsWriter<TestMessage>(participant, topic);

                // Write instances
                writer.Write(new TestMessage { Id = 100, Value = 1000 });
                writer.Write(new TestMessage { Id = 200, Value = 2000 });

                Thread.Sleep(200);

                // Dispose instances
                writer.DisposeInstance(new TestMessage { Id = 100, Value = 1000 });

                Thread.Sleep(200);

                Log("✓ Test_Instance_Dispose PASSED");
            }
            catch (Exception ex)
            {
                Fail($"Test_Instance_Dispose: {ex.Message}");
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // HELPER METHODS
        // ═══════════════════════════════════════════════════════════════

        static string GetUniqueTopic()
        {
            return $"LinuxTest_{Interlocked.Increment(ref _topicCounter)}_{Guid.NewGuid():N}";
        }

        static void Assert(bool condition, string message)
        {
            if (!condition)
            {
                throw new Exception($"Assertion failed: {message}");
            }
        }

        static void AssertEqual(int expected, int actual)
        {
            if (expected != actual)
            {
                throw new Exception($"Expected {expected}, got {actual}");
            }
        }

        static void Log(string message)
        {
            lock (_lock)
            {
                Console.WriteLine($"  {message}");
                _passed++;
            }
        }

        static void Fail(string message)
        {
            lock (_lock)
            {
                Console.WriteLine($"  ✗ {message}");
                _failed++;
            }
        }
    }
}
