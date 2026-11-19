namespace FlowBlox.Core.Util.Knowledge;

internal static class VectorMath
{
    internal static void Validate(float[] vector)
    {
        if (vector.Length == 0 || vector.Any(value => !float.IsFinite(value)) || vector.All(value => value == 0))
            throw new InvalidDataException("Embedding vectors must contain finite values and have a nonzero length and norm.");
    }

    internal static double Cosine(float[] left, float[] right)
    {
        if (left.Length != right.Length)
            throw new InvalidDataException("Embedding dimensions do not match. Reindex the collection with one embedding model.");

        double dot = 0;
        double leftNorm = 0;
        double rightNorm = 0;
        for (var index = 0; index < left.Length; index++)
        {
            dot += (double)left[index] * right[index];
            leftNorm += (double)left[index] * left[index];
            rightNorm += (double)right[index] * right[index];
        }

        return dot / Math.Sqrt(leftNorm * rightNorm);
    }
}
