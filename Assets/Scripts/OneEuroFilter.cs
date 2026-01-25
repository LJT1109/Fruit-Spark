using UnityEngine;
using System;

/// <summary>
/// One Euro Filter for smoothing data.
/// Ref: https://jaantollege.com/posts/one_euro_filter/
/// </summary>
public class OneEuroFilter
{
    public float minCutoff; // Min cutoff frequency in Hz
    public float beta;      // Cutoff slope
    public float dCutoff;   // Cutoff frequency for derivative in Hz

    private LowPassFilter xFilt;
    private LowPassFilter dxFilt;
    private float lastTime;

    public OneEuroFilter(float minCutoff = 1.0f, float beta = 0.0f, float dCutoff = 1.0f)
    {
        this.minCutoff = minCutoff;
        this.beta = beta;
        this.dCutoff = dCutoff;
        xFilt = new LowPassFilter();
        dxFilt = new LowPassFilter();
        lastTime = -1f;
    }

    public float Filter(float value, float timestamp = -1f)
    {
        // If no timestamp provided, use Time.time
        if (timestamp < 0) timestamp = Time.time;

        // Initialize if first time
        if (lastTime < 0)
        {
            lastTime = timestamp;
            return xFilt.Filter(value, Alpha(timestamp, dCutoff)); // arbitrary alpha for first point
        }

        // Compute frequency of updates
        float dt = timestamp - lastTime;
        // Avoid division by zero
        if (dt <= 0) dt = 0.00001f; 

        // 1. Estimate derivative of signal (velocity)
        // Default cutoff used for derivative is usually 1Hz (dCutoff)
        float dx = (value - xFilt.LastValue()) / dt;
        float edx = dxFilt.Filter(dx, Alpha(dt, dCutoff));

        // 2. Use derivative to dynamically tune cutoff frequency for signal
        // cutoff = minCutoff + beta * |edx|
        float cutoff = minCutoff + beta * Mathf.Abs(edx);

        // 3. Filter signal
        lastTime = timestamp;
        return xFilt.Filter(value, Alpha(dt, cutoff));
    }

    private float Alpha(float dt, float cutoff)
    {
        float tau = 1.0f / (2.0f * Mathf.PI * cutoff);
        return 1.0f / (1.0f + tau / dt);
    }
}

public class LowPassFilter
{
    private float y, s;
    private bool initialized = false;

    public float Filter(float value, float alpha)
    {
        if (!initialized)
        {
            s = value;
            y = value;
            initialized = true;
        }
        else
        {
            s = value;
            y = alpha * value + (1.0f - alpha) * y;
        }
        return y;
    }

    public float LastValue() { return y; }
}

/// <summary>
/// Helper class for Vector3 smoothing
/// </summary>
public class OneEuroFilter3
{
    private OneEuroFilter xFilt;
    private OneEuroFilter yFilt;
    private OneEuroFilter zFilt;

    public OneEuroFilter3(float minCutoff = 1.0f, float beta = 0.0f, float dCutoff = 1.0f)
    {
        xFilt = new OneEuroFilter(minCutoff, beta, dCutoff);
        yFilt = new OneEuroFilter(minCutoff, beta, dCutoff);
        zFilt = new OneEuroFilter(minCutoff, beta, dCutoff);
    }

    public Vector3 Filter(Vector3 value, float timestamp = -1f)
    {
        return new Vector3(
            xFilt.Filter(value.x, timestamp),
            yFilt.Filter(value.y, timestamp),
            zFilt.Filter(value.z, timestamp)
        );
    }
    
    public void UpdateParams(float minCutoff, float beta)
    {
        xFilt.minCutoff = minCutoff; xFilt.beta = beta;
        yFilt.minCutoff = minCutoff; yFilt.beta = beta;
        zFilt.minCutoff = minCutoff; zFilt.beta = beta;
    }
}
