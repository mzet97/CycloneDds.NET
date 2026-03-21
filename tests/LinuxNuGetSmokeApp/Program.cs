using CycloneDDS.Runtime;
using CycloneDDS.Schema;

namespace LinuxNuGetSmokeApp;

[DdsTopic("NuGetSmokeMessage")]
public partial struct SmokeMessage
{
    public int Id;
    public int Value;
}

public static class Program
{
    public static void Main()
    {
        var topic = $"NuGetSmoke_{Guid.NewGuid():N}";
        const int expectedId = 8011;
        const int expectedValue = 123456;

        Console.WriteLine("Starting Linux NuGet smoke test...");
        Console.WriteLine($"Topic: {topic}");

        using var participant = new DdsParticipant(0);
        using var writer = new DdsWriter<SmokeMessage>(participant, topic);
        using var reader = new DdsReader<SmokeMessage>(participant, topic);

        writer.Write(new SmokeMessage
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

        Console.WriteLine("SUCCESS: CycloneDDS NuGet Linux roundtrip OK.");
    }
}
