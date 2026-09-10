namespace IOTSnap.Hmi.Features.Designer;

public static class RuntimeTrendDownsampling
{
    public static IReadOnlyList<T> TakeEvenly<T>(IReadOnlyList<T> samples, int maximum)
    {
        if (maximum < 2 || samples.Count <= maximum)
        {
            return samples;
        }

        var result = new List<T>(maximum);
        for (var index = 0; index < maximum; index++)
        {
            var sourceIndex = (int)Math.Round(index * (samples.Count - 1d) / (maximum - 1d));
            result.Add(samples[sourceIndex]);
        }

        return result;
    }
}
