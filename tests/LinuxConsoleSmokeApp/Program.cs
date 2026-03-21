using CycloneDDS.Runtime;
using CycloneDDS.Runtime.Tests;

var topic = $"RootLinuxSmoke_{Guid.NewGuid():N}";
var expectedId = 7001;
var expectedValue = 99002;

Console.WriteLine("Starting Linux console smoke test...");
Console.WriteLine($"Topic: {topic}");

using var participant = new DdsParticipant(0);
using var writer = new DdsWriter<TestMessage>(participant, topic);
using var reader = new DdsReader<TestMessage>(participant, topic);

writer.Write(new TestMessage
{
    Id = expectedId,
    Value = expectedValue
});

Thread.Sleep(600);

using var samples = reader.Take();
if (samples.Count <= 0)
{
    throw new InvalidOperationException("No samples received.");
}

var foundValidSample = false;
for (var i = 0; i < samples.Count; i++)
{
    if (samples.Infos[i].ValidData == 0)
    {
        continue;
    }

    foundValidSample = true;
    if (samples[i].Id != expectedId || samples[i].Value != expectedValue)
    {
        throw new InvalidOperationException(
            $"Unexpected payload. Expected ({expectedId}, {expectedValue}), got ({samples[i].Id}, {samples[i].Value}).");
    }
}

if (!foundValidSample)
{
    throw new InvalidOperationException("Received samples, but none with valid data.");
}

Console.WriteLine("SUCCESS: CycloneDDS Linux console roundtrip OK.");
