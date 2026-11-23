using System.Buffers.Binary;
using System.Numerics;
using System.Text.Json.Nodes;

internal static class VillagerAnimationAssets
{
    private const float VectorTolerance = .0005f;
    // A half degree of angular error corresponds to half that angle in quaternion space.
    private static readonly float RotationTolerance = 1 - MathF.Cos(.25f * MathF.PI / 180);

    internal static byte[] Prepare(JsonNode model, ReadOnlySpan<byte> binary)
    {
        if (model["animations"] is not JsonArray animations) return [];

        var idle = animations.FirstOrDefault(a => (string?)a?["name"] == "Idle")
            ?? animations.FirstOrDefault(a => ((string?)a?["name"])?.StartsWith("Idle_", StringComparison.Ordinal) == true);
        for (int i = animations.Count - 1; i >= 0; i--)
            if (animations[i] != idle && (string?)animations[i]?["name"] is not ("Running" or "Walking"))
                animations.RemoveAt(i);

        var accessors = model["accessors"]!.AsArray();
        var views = model["bufferViews"]!.AsArray();
        using var extra = new MemoryStream();
        foreach (var animation in animations)
        foreach (var entry in animation!["samplers"]!.AsArray())
        {
            var sampler = entry!;
            string? interpolation = (string?)sampler["interpolation"];
            if (interpolation is not (null or "LINEAR" or "STEP")) continue;
            var times = accessors[(int)sampler["input"]!]!;
            var values = accessors[(int)sampler["output"]!]!;
            int count = (int)times["count"]!;
            int size = (string?)values["type"] switch { "VEC3" => 12, "VEC4" => 16, _ => 0 };
            if (count < 2 || size == 0 || (int)values["count"]! != count ||
                !AnimationData(times, views, 4, out int timeOffset, out int timeStride) ||
                !AnimationData(values, views, size, out int valueOffset, out int valueStride)) continue;

            var first = binary.Slice(valueOffset, size);
            int key = 1;
            while (key < count && first.SequenceEqual(binary.Slice(valueOffset + key * valueStride, size))) key++;
            if (key == count)
            {
                // Keep the original final time so the clip duration does not change.
                int lastOffset = (count - 1) * timeStride;
                float last = BinaryPrimitives.ReadSingleLittleEndian(binary.Slice(timeOffset + lastOffset, 4));
                var time = times.DeepClone();
                var value = values.DeepClone();
                time["count"] = 1;
                value["count"] = 1;
                time["byteOffset"] = ((int?)time["byteOffset"] ?? 0) + lastOffset;
                time["min"] = new JsonArray(last);
                time["max"] = new JsonArray(last);
                sampler["input"] = accessors.Count;
                accessors.Add(time);
                sampler["output"] = accessors.Count;
                accessors.Add(value);
            }
            else if (interpolation != "STEP")
            {
                var sampleTimes = new float[count];
                var samples = new Vector4[count];
                bool valid = true;
                float previousTime = -1;
                for (int i = 0; i < count; i++)
                {
                    float time = BinaryPrimitives.ReadSingleLittleEndian(binary.Slice(timeOffset + i * timeStride, 4));
                    var bytes = binary.Slice(valueOffset + i * valueStride, size);
                    var sample = new Vector4(BinaryPrimitives.ReadSingleLittleEndian(bytes),
                        BinaryPrimitives.ReadSingleLittleEndian(bytes[4..]),
                        BinaryPrimitives.ReadSingleLittleEndian(bytes[8..]),
                        size == 16 ? BinaryPrimitives.ReadSingleLittleEndian(bytes[12..]) : 0);
                    float length = sample.LengthSquared();
                    if (!float.IsFinite(time) || time < 0 || time <= previousTime ||
                        !float.IsFinite(length) || size == 16 && length < 1e-12f)
                    {
                        valid = false;
                        break;
                    }
                    sampleTimes[i] = previousTime = time;
                    samples[i] = sample;
                }
                if (!valid) continue;
                int[] keys = KeepKeys(sampleTimes, samples, size == 16);
                if (keys.Length == count) continue;
                sampler["input"] = AppendAccessor(times, accessors, views, extra, binary, timeOffset, timeStride, 4, keys);
                sampler["output"] = AppendAccessor(values, accessors, views, extra, binary, valueOffset, valueStride, size, keys);
            }
        }
        if (idle != null) idle["name"] = "Idle";
        return extra.ToArray();
    }

    private static int[] KeepKeys(float[] times, Vector4[] values, bool rotation)
    {
        float tolerance = rotation ? RotationTolerance : VectorTolerance * VectorTolerance;
        var keep = new bool[times.Length];
        keep[0] = keep[^1] = true;
        var ranges = new Stack<(int First, int Last)>();
        ranges.Push((0, times.Length - 1));
        if (rotation)
            for (int i = 0; i < values.Length; i++) values[i] = Vector4.Normalize(values[i]);

        // Refine each segment where it differs most from the original samples.
        while (ranges.TryPop(out var range))
        {
            var (first, last) = range;
            float greatest = tolerance;
            int split = -1;
            for (int i = first + 1; i < last; i++)
            {
                float t = (times[i] - times[first]) / (times[last] - times[first]);
                float error = rotation
                    ? RotationError(values[first], values[last], values[i], t)
                    : Vector4.DistanceSquared(Vector4.Lerp(values[first], values[last], t), values[i]);
                if (error > greatest)
                {
                    greatest = error;
                    split = i;
                }
            }
            if (split < 0) continue;
            keep[split] = true;
            ranges.Push((first, split));
            ranges.Push((split, last));
        }
        return Enumerable.Range(0, keep.Length).Where(i => keep[i]).ToArray();
    }

    private static float RotationError(Vector4 first, Vector4 last, Vector4 actual, float t)
    {
        var expected = Quaternion.Slerp(new Quaternion(first.X, first.Y, first.Z, first.W),
            new Quaternion(last.X, last.Y, last.Z, last.W), t);
        return 1 - MathF.Abs(Vector4.Dot(Vector4.Normalize(new Vector4(expected.X, expected.Y, expected.Z, expected.W)), actual));
    }

    private static int AppendAccessor(JsonNode source, JsonArray accessors, JsonArray views,
        MemoryStream extra, ReadOnlySpan<byte> binary, int offset, int stride, int size, int[] keys)
    {
        var accessor = source.DeepClone();
        accessor["count"] = keys.Length;
        accessor["byteOffset"] = 0;
        accessor["bufferView"] = views.Count;
        if (size != 4)
        {
            accessor.AsObject().Remove("min");
            accessor.AsObject().Remove("max");
        }
        views.Add(new JsonObject
        {
            ["buffer"] = 0,
            ["byteOffset"] = binary.Length + (int)extra.Length,
            ["byteLength"] = keys.Length * size
        });
        foreach (int key in keys) extra.Write(binary.Slice(offset + key * stride, size));
        int index = accessors.Count;
        accessors.Add(accessor);
        return index;
    }

    private static bool AnimationData(JsonNode accessor, JsonArray views, int size, out int offset, out int stride)
    {
        offset = 0;
        stride = size;
        if ((int?)accessor["componentType"] != 5126 || accessor["sparse"] != null || accessor["bufferView"] == null) return false;
        var view = views[(int)accessor["bufferView"]!]!;
        if ((int?)view["buffer"] != 0) return false;
        offset = ((int?)view["byteOffset"] ?? 0) + ((int?)accessor["byteOffset"] ?? 0);
        stride = (int?)view["byteStride"] ?? size;
        return true;
    }
}
