"""Synthetic CSV interpretation checks; not game or native-rig evidence."""
import csv
import importlib.util
import tempfile
import unittest
from pathlib import Path

spec = importlib.util.spec_from_file_location("harness_trace_analysis", Path(__file__).with_name("analyze_harness_trace.py"))
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)


class HarnessTraceTests(unittest.TestCase):
    def analyze(self, mutate=lambda rows: None):
        rows = [{"frame": 10+i, "t": i/100, "dt": .01,
                 "L0clip": "synthetic@1.00", "L0norm": (i//2)*.02,
                 "L0spd": 1, "L0trans": 0, "HipsX": 0, "HipsY": 1, "HipsZ": 0}
                for i in range(7)]
        mutate(rows)
        with tempfile.TemporaryDirectory() as folder:
            path = Path(folder)/"synthetic.csv"
            with path.open("w",newline="",encoding="utf-8") as stream:
                writer=csv.DictWriter(stream,fieldnames=rows[0])
                writer.writeheader()
                writer.writerows(rows)
            return module.summarize(path)

    def test_fixed_pose_updates_and_render_callbacks_are_distinct(self):
        result = self.analyze()
        self.assertAlmostEqual(result["render_update"]["observed_callbacks_per_second"], 100)
        self.assertAlmostEqual(result["render_update"]["changed_base_state_time_intervals_per_second"], 50)
        self.assertAlmostEqual(result["inferred_playback"][0]["normalized_state_units_per_wall_second"], 1)
        self.assertIn("NOT_BASELINE_ACCEPTANCE", result["status"])

    def test_duplicate_clip_names_remain_explicitly_ambiguous(self):
        self.assertEqual(module.clip_names("same@0.60+same@0.40"), {"same":1.0})
        self.assertIn("source sampling", self.analyze()["unavailable"][0])

    def test_duplicate_frame_is_rejected(self):
        with self.assertRaisesRegex(ValueError,"Duplicate"):
            self.analyze(lambda rows: rows[1].update(frame=rows[0]["frame"]))

    def test_nonfinite_time_is_rejected(self):
        with self.assertRaisesRegex(ValueError,"Non-finite"):
            self.analyze(lambda rows: rows[1].update(t="nan"))

    def test_transition_is_not_reported_as_steady_playback(self):
        result=self.analyze(lambda rows: [r.update(L0trans=1) for r in rows])
        self.assertEqual(result["inferred_playback"], [])

    def test_evaluated_pose_does_not_become_rest_or_contact_measurement(self):
        result=self.analyze()
        self.assertIn("not rest pose", result["observed_bone_ranges"]["Hips"]["limit"])


if __name__ == "__main__":
    unittest.main()
