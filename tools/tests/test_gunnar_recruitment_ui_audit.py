"""#460: inspect ONLY new GUI cleanup ownership, not a body-removal grant.
Text audit: it does not follow aliases/delegates or prove control flow. The real
validator still needs its explicit GUI population pin from the integration lead.
"""
from pathlib import Path
import re
import unittest
ROOT = Path(__file__).resolve().parents[2]
PANEL = ROOT / 'src/ConcernedTeamster/Ui/Companions/GunnarRecruitmentPanel.cs'

def errors(source):
    found = []
    calls = re.findall(r'(?:[A-Za-z_]\w*\s*\.\s*)*Destroy(?:Immediate)?\s*\([^;]*\)', source)
    normal = [re.sub(r'\s+', '', call) for call in calls]
    if sorted(normal) != sorted(['UnityEngine.Object.Destroy(_panel)', 'UnityEngine.Object.Destroy(_button)']):
        found.append('cleanup must destroy only its two owned GUI objects')
    allowed = {'_button': ('GUIManager.Instance.CreateButton(', 'null;'),
               '_panel': ('gui.CreateWoodpanel(', '_button = null;')}
    for field, expected in allowed.items():
        assignments = re.findall(r'\b' + field + r'\s*=(?!=)\s*([^\n]+)', source)
        if len(assignments) != 2 or not all(any(rhs.startswith(prefix) for prefix in expected) for rhs in assignments):
            found.append(field + ' was not assigned only from GUI construction and cleanup')
    return found

class RecruitmentGuiCleanup(unittest.TestCase):
    def test_actual_source_owns_both_destroyed_widgets(self):
        self.assertEqual([], errors(PANEL.read_text(encoding='utf-8-sig')))
    def test_planted_body_destroy_is_refused(self):
        source = PANEL.read_text(encoding='utf-8-sig')
        self.assertTrue(errors(source.replace('Object.Destroy(_panel)', 'Object.Destroy(otherBody)')))
    def test_planted_receiver_replacement_is_refused(self):
        source = PANEL.read_text(encoding='utf-8-sig')
        self.assertTrue(errors(source.replace('if (_panel != null) UnityEngine.Object.Destroy(_panel);',
            '_panel = otherBody;\n        if (_panel != null) UnityEngine.Object.Destroy(_panel);')))
    def test_immediate_destroy_is_not_the_same_cleanup(self):
        self.assertTrue(errors(PANEL.read_text(encoding='utf-8-sig').replace('Object.Destroy(_panel)', 'Object.DestroyImmediate(_panel)')))

if __name__ == '__main__':
    unittest.main()
