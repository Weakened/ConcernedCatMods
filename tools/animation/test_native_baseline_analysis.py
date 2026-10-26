"""Synthetic parser tests. These fixtures are not Valheim or rig evidence."""
import copy
import importlib.util
import unittest
from pathlib import Path

spec = importlib.util.spec_from_file_location("baseline_analysis", Path(__file__).with_name("analyze_native_baseline.py"))
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)


def fixture():
    records = [
        {"kind": "header", "schema": 1, "action": "synthetic_walk",
         "isolationEvidenceClaim": "SYNTHETIC_TEST_NOT_GAME_EVIDENCE", "nativeIKPatchOwners": []},
        {"kind": "clip", "id": 7, "name": "synthetic_clip", "lengthSeconds": 2,
         "authoredSamplingRate": 24, "looping": True}
    ]
    for i in range(5):
        records.append({
            "kind": "frame", "stage": "synthetic", "frame": 100 + i,
            "elapsedSeconds": 1 + i / 50, "unityUnscaledDeltaSeconds": 1 / 50,
            "readMilliseconds": 0.1, "animatorSpeed": 1, "timeScale": 1,
            "layers": [{"index": 0, "name": "synthetic_base", "transitioning": False,
                        "current": {"fullPathHash": 13, "normalizedTime": i / 100,
                                    "lengthSeconds": 2, "speed": 1, "speedMultiplier": 1},
                        "currentClips": [{"id": 7, "weight": 1}], "nextClips": []}]
        })
    records.append({"kind": "end", "sampledFrames": 5, "reason": "lead_finished"})
    return records


class BaselineAnalysisTests(unittest.TestCase):
    def test_source_sampling_is_kept_separate_from_render_and_state_rate(self):
        report = module.summarize(fixture())
        self.assertEqual(report["observed_clip_metadata"][0]["authoredSamplingRate"], 24)
        self.assertAlmostEqual(report["observed_render_update_cadence"]["callbacks_per_second"], 50)
        self.assertAlmostEqual(report["inferred_state_playback"][0]
                               ["normalized_state_cycles_per_unscaled_second"], 0.5)
        self.assertIn("MEASUREMENTS_ONLY", report["status"])
        self.assertIsNone(report["inferred_state_playback"][0]["readable_state_name"])

    def test_duplicate_frame_is_not_an_additional_render_observation(self):
        data = fixture()
        data[3]["frame"] = data[2]["frame"]
        with self.assertRaisesRegex(ValueError, "Duplicate"):
            module.summarize(data)

    def test_rejected_custom_hook_prevents_native_baseline_label(self):
        data = fixture()
        data[0]["nativeIKPatchOwners"] = ["com.theconcernedcat.valheim.concernedladders.pose"]
        with self.assertRaisesRegex(ValueError, "not an isolated native"):
            module.summarize(data)

    def test_unknown_clip_identity_is_not_silently_joined_by_name(self):
        data = fixture()
        data[2]["layers"][0]["currentClips"][0]["id"] = 99
        with self.assertRaisesRegex(ValueError, "absent"):
            module.summarize(data)

    def test_transition_samples_are_not_steady_state_cadence(self):
        data = fixture()
        for row in data[2:-1]:
            row["layers"][0]["transitioning"] = True
        self.assertEqual(module.summarize(data)["inferred_state_playback"], [])

    def test_different_states_are_not_combined_into_one_cycle(self):
        data = fixture()
        for i, row in enumerate(data[2:-1]):
            row["layers"][0]["current"]["fullPathHash"] = 100 + i
        self.assertEqual(module.summarize(data)["inferred_state_playback"], [])

    def test_render_gaps_are_reported_and_excluded_from_cadence_estimate(self):
        data = fixture()
        for i, row in enumerate(data[2:-1]):
            row["frame"] += i
        report = module.summarize(data)
        self.assertEqual(report["observed_render_update_cadence"]["missing_frame_indices"], 4)
        self.assertEqual(report["inferred_state_playback"], [])

    def test_truncated_or_nonfinite_records_do_not_yield_a_measurement_report(self):
        data = fixture()
        with self.assertRaisesRegex(ValueError, "end record"):
            module.summarize(data[:-1])
        bad = copy.deepcopy(data)
        bad[2]["unityUnscaledDeltaSeconds"] = float("nan")
        with self.assertRaisesRegex(ValueError, "finite"):
            module.summarize(bad)
        bad = copy.deepcopy(data)
        bad[-1]["sampledFrames"] = 6
        with self.assertRaisesRegex(ValueError, "matching end"):
            module.summarize(bad)

    def test_container_rate_is_not_assumed_valid_when_zero_or_invalid(self):
        self.assertAlmostEqual(module.rational_rate("30000/1001"), 29.97002997002997)
        for invalid in ("0/0", "0/1", "invalid", None):
            self.assertIsNone(module.rational_rate(invalid))


if __name__ == "__main__":
    unittest.main()
