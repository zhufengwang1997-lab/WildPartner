import csv, os, sys, tempfile, unittest
import numpy as np
from PIL import Image
sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
import quality_eval as qe
import bench_report as br


def save(path, arr):
    Image.fromarray((np.clip(arr, 0, 1) * 255).astype(np.uint8)).save(path)


class QualityEvalTests(unittest.TestCase):
    def test_identical_images(self):
        a = np.random.RandomState(0).rand(32, 48, 3).astype(np.float32)
        self.assertEqual(qe.psnr(a, a), float("inf"))
        self.assertAlmostEqual(qe.ssim(a, a), 1.0, places=5)
        m, _ = qe.flip(a, a)
        self.assertAlmostEqual(m, 0.0, places=5)

    def test_worse_image_scores_worse(self):
        rs = np.random.RandomState(1)
        ref = rs.rand(64, 64, 3).astype(np.float32)
        small = np.clip(ref + rs.normal(0, 0.02, ref.shape), 0, 1).astype(np.float32)
        big = np.clip(ref + rs.normal(0, 0.2, ref.shape), 0, 1).astype(np.float32)
        self.assertGreater(qe.psnr(ref, small), qe.psnr(ref, big))
        self.assertGreater(qe.ssim(ref, small), qe.ssim(ref, big))
        self.assertLess(qe.flip(ref, small)[0], qe.flip(ref, big)[0])

    def test_evaluate_directory(self):
        d = tempfile.mkdtemp()
        ref = np.full((20, 30, 3), 0.5, np.float32)
        save(os.path.join(d, "SC-01_ref.png"), ref)
        save(os.path.join(d, "SC-01_native_taa.png"), ref)
        save(os.path.join(d, "SC-01_STP_Quality.png"), ref * 0.9)
        rows = qe.evaluate(d, d, use_flip=False)
        self.assertEqual({r["config"] for r in rows}, {"native_taa", "STP_Quality"})
        self.assertEqual(qe.split_name("x/SC-02_FSR1_Balanced.png"), ("SC-02", "FSR1_Balanced"))


class BenchReportTests(unittest.TestCase):
    def make(self, d):
        with open(os.path.join(d, "bench.csv"), "w") as f:
            f.write("# gpu=TestGPU;api=Metal;display=100x50;tier=High;unity=6;date=x\n")
            f.write("scene,config,backend,quality,aa,render_w,render_h,display_w,display_h,gpu_ms,cpu_ms,frame_ms,upscale_ms,gfx_mem_mb,temporal_diff,image\n")
            for sc in ("SC-01", "SC-02"):
                f.write(f"{sc},ref,Off,NativeAA,Off,200,100,100,50,9,1,9,0,500,,a\n")
                f.write(f"{sc},native_taa,Off,NativeAA,TAA,100,50,100,50,5,1,5,0,400,0.01,a\n")
                f.write(f"{sc},STP_Quality,STP,Quality,TAA,67,33,100,50,4,1,4,0.3,380,0.012,a\n")
                f.write(f"{sc},FSR1_Performance,FSR1,Performance,TAA,50,25,100,50,3,1,3,0.2,360,0.03,a\n")
        with open(os.path.join(d, "quality.csv"), "w") as f:
            w = csv.writer(f); w.writerow(["scene", "config", "flip", "ssim", "psnr"])
            for sc in ("SC-01", "SC-02"):
                w.writerow([sc, "native_taa", 0.10, 0.9, 30]); w.writerow([sc, "STP_Quality", 0.105, 0.88, 29]); w.writerow([sc, "FSR1_Performance", 0.2, 0.7, 24])

    def test_summary_and_recommendation(self):
        d = tempfile.mkdtemp(); self.make(d)
        meta, rows = br.read_bench(os.path.join(d, "bench.csv"))
        self.assertEqual(meta["gpu"], "TestGPU")
        s = br.summarise(rows, br.read_quality(os.path.join(d, "quality.csv")))
        self.assertAlmostEqual(s["STP_Quality"]["fps"], 250.0)
        self.assertAlmostEqual(s["native_taa"]["temporal"], 0.01)
        rec, why = br.recommend(s)
        self.assertEqual(rec, "STP_Quality")          # within 110 % of native FLIP and faster; FSR1 Performance is too blurry

    def test_report_file(self):
        d = tempfile.mkdtemp(); self.make(d)
        out = os.path.join(d, "report.md")
        br.report(d, out)
        text = open(out, encoding="utf-8").read()
        self.assertIn("STP_Quality", text); self.assertIn("默认配置建议", text)


if __name__ == "__main__":
    unittest.main()
