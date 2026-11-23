using System;
using System.Collections;
using UnityEngine;

namespace WildPartner.Rendering
{
    /// <summary>
    /// Frame time measurement for the smoke test, the effects cost list and UpscaleBench: v-sync off, uncapped, then
    /// <see cref="FrameTimingManager"/> GPU and CPU times averaged (median-ish: the slowest 10 % dropped). Falls back
    /// to wall-clock frame time when the platform reports no GPU timings. v-sync is restored afterwards: with it on,
    /// every mode reads the display interval (8.3 ms at 120 Hz), which is what the first AA check measured.
    /// </summary>
    public static class GpuTimer
    {
        public struct Result { public float gpuMs, cpuMs, frameMs; public int frames; public bool gpuValid; public override string ToString() => gpuValid ? $"GPU {gpuMs:0.00} ms" : $"帧 {frameMs:0.00} ms"; }

        public static IEnumerator Measure(float seconds, Action<Result> done, float warmup = 0.6f)
        {
            int vs = QualitySettings.vSyncCount, tf = Application.targetFrameRate;
            QualitySettings.vSyncCount = 0; Application.targetFrameRate = -1;
            float t = 0; while (t < warmup) { t += Time.unscaledDeltaTime; FrameTimingManager.CaptureFrameTimings(); yield return null; }
            var gpu = new System.Collections.Generic.List<float>(); var cpu = new System.Collections.Generic.List<float>();
            var buf = new FrameTiming[1];
            int frames = 0; float t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < seconds)
            {
                FrameTimingManager.CaptureFrameTimings();
                if (FrameTimingManager.GetLatestTimings(1, buf) > 0)
                {
                    if (buf[0].gpuFrameTime > 0) gpu.Add((float)buf[0].gpuFrameTime);
                    if (buf[0].cpuFrameTime > 0) cpu.Add((float)buf[0].cpuFrameTime);
                }
                frames++; yield return null;
            }
            float wall = (Time.realtimeSinceStartup - t0) * 1000f / Mathf.Max(frames, 1);
            QualitySettings.vSyncCount = vs; Application.targetFrameRate = tf;
            done(new Result { gpuMs = Trim(gpu), cpuMs = Trim(cpu), frameMs = wall, frames = frames, gpuValid = gpu.Count > frames / 2 });
        }

        static float Trim(System.Collections.Generic.List<float> v)
        {
            if (v.Count == 0) return 0;
            v.Sort();
            int n = Mathf.Max(1, Mathf.FloorToInt(v.Count * 0.9f));
            double s = 0; for (int i = 0; i < n; i++) s += v[i];
            return (float)(s / n);
        }
    }
}
