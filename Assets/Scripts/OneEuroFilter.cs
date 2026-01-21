using UnityEngine;

public class OneEuroFilter
{
    private float freq;
    private float mincutoff;
    private float beta;
    private float dcutoff;

    private float lastTime;
    private Vector2 lastValue;
    private Vector2 lastDerivative;
    private bool isInitialized = false;

    public OneEuroFilter(float mincutoff = 1.0f, float beta = 0.0f, float dcutoff = 1.0f)
    {
        this.mincutoff = mincutoff;
        this.beta = beta;
        this.dcutoff = dcutoff;
        this.freq = 30f; // Default frequency, will be updated
    }

    public Vector2 Filter(Vector2 value, float timestamp)
    {
        if (lastTime != 0 && timestamp != -1)
        {
            freq = 1.0f / (timestamp - lastTime);
        }
        lastTime = timestamp;

        if (!isInitialized)
        {
            lastValue = value;
            lastDerivative = Vector2.zero;
            isInitialized = true;
            return value;
        }

        Vector2 derivative = (value - lastValue) * freq;
        Vector2 edx = LowPassFilter(derivative, lastDerivative, Alpha(dcutoff));
        lastDerivative = edx;

        float cutoff = mincutoff + beta * edx.magnitude;
        Vector2 result = LowPassFilter(value, lastValue, Alpha(cutoff));
        lastValue = result;

        return result;
    }

    private Vector2 LowPassFilter(Vector2 x, Vector2 lastX, float alpha)
    {
        return x * alpha + lastX * (1.0f - alpha);
    }

    private float Alpha(float cutoff)
    {
        float te = 1.0f / freq;
        float tau = 1.0f / (2 * Mathf.PI * cutoff);
        return 1.0f / (1.0f + tau / te);
    }

    public void Reset()
    {
        isInitialized = false;
        lastTime = 0;
    }
}
