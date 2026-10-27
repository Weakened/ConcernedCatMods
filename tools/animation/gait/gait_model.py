"""Pure-Python half of the Concerned Ladders animation pipeline (no Blender needed).

It holds three things, all shared by the Blender scripts, the exporter and the compiler:

* the data contract: the shape of a climbing loop and of a transition, exactly as the C# types
  ``ClimbLoop``/``GaitShape`` (Domain/Animation/LimbGait.cs) and ``TransitionAction``
  (Domain/Animation/TransitionAction.cs) hold them;
* the C# defaults reproduced sample for sample (``default_shape``, ``default_transitions``,
  ``default_constants``), which seed a fresh LadderGait.blend so a no-edit round trip regenerates them;
* the preview: a port of ``LimbGait`` (rung schedule, swing, body curve) and of the presenter's contact
  maths, used to pose the proxy climber in Blender and to fill the per-frame CSV tables.

The preview is a picture of the authored channels on a reference ladder, not the game: the in-game
presenter fits the same channels to the live rig, the real rung profile and the runtime's route.
"""
import math

# ---------------------------------------------------------------------------------------------- contract
LIMBS = ('LH', 'RH', 'LF', 'RF')
LIMB_NAMES = ('left hand', 'right hand', 'left foot', 'right foot')
ARC_SAMPLES = 33       # ClimbLoop.Arc / Swing samples over a limb's swing progress
BODY_SAMPLES = 61      # ClimbLoop.Body / Lean samples over the cycle
ACTION_SAMPLES = 41    # TransitionAction samples over progress

LOOP_NAMES = ('climb_up', 'climb_down')
# Order of GeneratedGait.Transitions(), the same as DefaultTransitions.All().
TRANSITION_KINDS = ('GroundMount', 'AirCatch', 'TopExit', 'SideExit', 'BottomStepOff', 'TopEntry')
EXIT_KINDS = ('TopExit', 'SideExit', 'BottomStepOff')

SHAPE_SCALARS = (  # GaitShape field, json key, default (LimbGait.cs + the GeneratedGait override)
    ('FootLowRel', 'foot_low_rel', -0.06),
    ('HandMidFromShoulder', 'hand_mid_from_shoulder', -0.02),
    ('HandSettleBelow', 'hand_settle_below', 0.38),
    ('HandSettleAbove', 'hand_settle_above', 0.38),
    ('FootSettleBelow', 'foot_settle_below', 0.1),
    ('FootSettleAbove', 'foot_settle_above', 0.75),
    ('SettleSeconds', 'settle_seconds', 0.18),
    ('RejoinSeconds', 'rejoin_seconds', 0.12),
    ('ReverseSeconds', 'reverse_seconds', 0.3),
)

POSTURE_CONSTANTS = (  # GeneratedGait constant, property on CTL_Pelvis, default
    ('PelvisDrop', 'pelvis_drop', 0.0),
    ('PelvisToward', 'pelvis_toward', 0.12),
    ('Lean', 'lean', 12.0),
    ('SlideLean', 'slide_lean', 6.0),
    ('SwayTilt', 'sway_tilt', 3.0),
    ('SwayShift', 'sway_shift', 0.03),
)
BEND_CONSTANTS = (  # GeneratedGait constant, property on CTL_Bend, default
    ('ElbowOut', 'elbow_out', 0.18),
    ('ElbowSide', 'elbow_side', 0.08),
    ('ElbowDown', 'elbow_down', 0.32),
    ('KneeToward', 'knee_toward', 0.22),
    ('KneeSide', 'knee_side', 0.04),
    ('KneeDown', 'knee_down', 0.15),
)


def f32(value):
    """Round through IEEE single precision (what C# float and Blender keyframes hold)."""
    import struct
    return struct.unpack('<f', struct.pack('<f', float(value)))[0]


# ------------------------------------------------------------------------------------- C# reproductions
def smooth(count):
    return [(lambda t: t * t * (3.0 - 2.0 * t))(i / (count - 1)) for i in range(count)]


def arc3(count, lat, outward, up):
    rows = []
    for i in range(count):
        s = math.sin(math.pi * i / (count - 1))
        rows.append([lat * s * s, outward * s * s, up * s * s])
    return rows


def flat(count, value):
    return [value] * count


def dwell(samples, amount, foot_starts, foot_length, slow_at, cycle_metres=1.0):
    """ClimbLoop.Dwell: zero-mean running integral of a slow-then-rise body speed."""
    up = [0.0] * samples
    step = 1.0 / (samples - 1)
    offset = total = 0.0
    for i in range(1, samples):
        t = (i - 0.5) * step
        speed = 0.0
        for start in foot_starts:
            d = t - start
            d -= math.floor(d)
            if d < foot_length:
                speed -= math.cos(2 * math.pi * (d / foot_length - slow_at))
        offset += speed * amount * cycle_metres * step
        up[i] = offset
        total += offset
    mean = total / (samples - 1)
    table = [[up[i] - mean, 0.0] for i in range(samples)]
    table[0][0] = table[samples - 1][0]
    return table


def loop(name, start, length, arcs, body, stride=2):
    return {'name': name, 'strideRungs': stride, 'start': list(start), 'length': list(length),
            'swing': smooth(ARC_SAMPLES), 'arc': arcs, 'body': body, 'lean': flat(BODY_SAMPLES, 0.0)}


def default_up():
    return loop('climb_up', (0.50, 0.00, 0.20, 0.70), (0.40, 0.40, 0.50, 0.50),
                [arc3(ARC_SAMPLES, 0.0, 0.07, 0.02), arc3(ARC_SAMPLES, 0.0, 0.07, 0.02),
                 arc3(ARC_SAMPLES, 0.01, 0.14, 0.05), arc3(ARC_SAMPLES, 0.01, 0.14, 0.05)],
                dwell(BODY_SAMPLES, 0.6, (0.20, 0.70), 0.50, 0.3))


def default_down():
    return loop('climb_down', (0.10, 0.60, 0.62, 0.12), (0.40, 0.40, 0.44, 0.44),
                [arc3(ARC_SAMPLES, 0.0, 0.06, 0.01), arc3(ARC_SAMPLES, 0.0, 0.06, 0.01),
                 arc3(ARC_SAMPLES, 0.0, 0.09, 0.0), arc3(ARC_SAMPLES, 0.0, 0.09, 0.0)],
                dwell(BODY_SAMPLES, 0.5, (0.62, 0.12), 0.44, 0.85))


def default_shape():
    shape = {key: value for _, key, value in SHAPE_SCALARS}
    shape['up'] = default_up()
    shape['down'] = default_down()
    return shape


def default_constants():
    return {name: value for name, _, value in POSTURE_CONSTANTS + BEND_CONSTANTS}


class Builder:
    """TransitionAction.Builder: smoothstep keys and sin^2 arcs over ACTION_SAMPLES samples."""

    def __init__(self, kind, seconds, samples=ACTION_SAMPLES):
        self.n = samples
        self.a = {'kind': kind, 'seconds': seconds,
                  'weight': [[1.0] * samples for _ in range(4)], 'reach': [[0.0] * samples for _ in range(4)],
                  'arc': [[[0.0, 0.0, 0.0] for _ in range(samples)] for _ in range(4)],
                  'bodyWeight': [1.0] * samples, 'bodyUp': [0.0] * samples, 'bodyToward': [0.0] * samples,
                  'lean': [0.0] * samples}

    def _keys(self, table, keys):
        for i in range(self.n):
            t = i / (self.n - 1)
            value = keys[1]
            for k in range(0, len(keys) - 2, 2):
                if keys[k] <= t <= keys[k + 2]:
                    u = (t - keys[k]) / (keys[k + 2] - keys[k]) if keys[k + 2] > keys[k] else 1.0
                    u = u * u * (3.0 - 2.0 * u)
                    value = keys[k + 1] + (keys[k + 3] - keys[k + 1]) * u
                    break
                if t > keys[k + 2]:
                    value = keys[k + 3]
            table[i] = value
        return self

    def weight(self, limb, *keys): return self._keys(self.a['weight'][limb], keys)
    def reach(self, limb, *keys): return self._keys(self.a['reach'][limb], keys)
    def body(self, *keys): return self._keys(self.a['bodyWeight'], keys)
    def body_up(self, *keys): return self._keys(self.a['bodyUp'], keys)
    def body_toward(self, *keys): return self._keys(self.a['bodyToward'], keys)
    def lean(self, *keys): return self._keys(self.a['lean'], keys)

    def arc(self, limb, start, end, lat, outward, up):
        for i in range(self.n):
            t = i / (self.n - 1)
            if t <= start or t >= end:
                continue
            s = math.sin(math.pi * (t - start) / (end - start))
            s *= s
            row = self.a['arc'][limb][i]
            row[0] += lat * s
            row[1] += outward * s
            row[2] += up * s
        return self

    def build(self):
        return self.a


def default_transitions():
    """DefaultTransitions.All(), in its order, read from the C# source (Domain/Animation/TransitionAction.cs) so
    the transitions are authored in one place until the .blend takes over: each Builder chain is replayed here."""
    import os
    import re
    path = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', '..', '..', 'src', 'ConcernedLadders',
                        'Domain', 'Animation', 'TransitionAction.cs')
    source = open(path, encoding='utf-8').read()
    body = source[source.index('internal static TransitionAction[] All()'):]
    limbs = {'LH': 0, 'RH': 1, 'LF': 2, 'RF': 3}
    methods = {'Weight': 'weight', 'Reach': 'reach', 'Arc': 'arc', 'Body': 'body', 'BodyUp': 'body_up',
               'BodyToward': 'body_toward', 'Lean': 'lean'}

    def number(token):
        token = token.strip()
        return limbs[token] if token in limbs else float(token.rstrip('fF'))

    actions = []
    for chain in re.finditer(r'new TransitionAction\.Builder\(TransitionKind\.(\w+),\s*([-\d.]+)f?\)(.*?)\.Build\(\)', body, re.S):
        builder = Builder(chain.group(1), float(chain.group(2)))
        code = re.sub(r'//[^\n]*', '', chain.group(3))
        for call in re.finditer(r'\.(\w+)\(([^)]*)\)', code):
            if call.group(1) not in methods:
                raise ValueError('unknown Builder call ' + call.group(1))
            getattr(builder, methods[call.group(1)])(*[number(a) for a in call.group(2).split(',')])
        actions.append(builder.build())
    if [a['kind'] for a in actions] != list(TRANSITION_KINDS):
        raise ValueError('DefaultTransitions order changed: ' + str([a['kind'] for a in actions]))
    return actions


def default_gait():
    """Everything GeneratedGait carries, as the exporter's JSON lays it out (minus provenance)."""
    return {'shape': default_shape(), 'transitions': default_transitions(), 'constants': default_constants()}


# ------------------------------------------------------------------------------------------ sampling
def sample(table, x, column=None):
    """ClimbLoop.Sample: clamp, linear between samples."""
    position = max(0.0, min(1.0, x)) * (len(table) - 1)
    index = min(len(table) - 2, int(math.floor(position)))
    a, b = table[index], table[index + 1]
    if column is not None:
        a, b = a[column], b[column]
    return a + (b - a) * (position - index)


def ease5(value):
    t = max(0.0, min(1.0, value))
    return t * t * t * (10.0 + t * (-15.0 + 6.0 * t))


def smooth01(value):
    t = max(0.0, min(1.0, value))
    return t * t * (3.0 - 2.0 * t)


# ------------------------------------------------------------------------------- reference scene (metres)
# Blender world: the rungs' centre plane is y = 0, the climber is on +Y facing -Y (toward the ladder), up is +Z,
# the climber's left is +X (Blender's .L side). "out" = +Y (away from the rungs), "toward" = -Y.
PITCH = 1.0 / 3.0                 # LadderPieceRegistration: RungPitch
RUNG_BASE = PITCH * 0.5           # rung 0 centre above the ground
RUNG_LAST = 14                    # 15 rungs: top rung 4.83 m, rail tops at LADDER_TOP
LADDER_TOP = 5.0                  # the upper floor's surface
SIDE_FLOOR = 2.0                  # the intermediate (side) floor's surface, to the climber's left
RAIL_X = 0.36
RUNG_HALF_WIDTH = 0.31            # ClimbPresentation.HalfWidth
RUNG_HALF_HEIGHT = 0.04
RUNG_HALF_DEPTH = 0.06
ROOT_OUT = 0.5                    # body root (feet) from the rung plane: ClimbPolicy lane = rail depth + capsule radius + LaneGap
PELVIS_HEIGHT = 0.837             # OriginalRigBinding.PelvisHeight for the native rig (0.93 x 0.9 m leg chain)
SHOULDER_REL = 1.38               # ShoulderHeight above the feet (as the gait tests bind)
HAND_ABOVE, HAND_OUT, HAND_SPAN = 0.055, 0.025, 0.19     # ClimbTuning
FOOT_ABOVE, FOOT_OUT, FOOT_SPAN = 0.045, 0.0, 0.128      # ClimbTuning
PALM_THICKNESS, EXIT_HAND_AHEAD = 0.02, -0.45            # ClimbTuning
PROXY_SHOULDER_ABOVE_HIPS = 0.48  # the proxy's shoulder joints above its hips bone
PROXY_SHOULDER_HALF, PROXY_HIP_HALF = 0.19, 0.1
LOOP_CYCLES = 2                   # a loop preview starts with the feet this many cycles above rung 0 (phase 0)

# Per transition, the preview's route of the body root (feet): a start, an optional corner reached at a fraction of the
# progress, an end; and the floor contact point (the landing, or the floor a top entry starts from). The runtime's
# route comes from the game; these stand in for it on the reference ladder.
SCENARIOS = {
    'GroundMount': {'route': ((0.0, ROOT_OUT + 0.25, 0.0), None, (0.0, ROOT_OUT, 0.15)), 'floor': None},
    'AirCatch': {'route': ((0.0, ROOT_OUT + 0.15, 2.35), None, (0.0, ROOT_OUT, 2.0)), 'floor': None},
    'TopExit': {'route': ((0.0, ROOT_OUT, LADDER_TOP - 1.2), ((0.0, ROOT_OUT, LADDER_TOP), 0.55), (0.0, -0.55, LADDER_TOP)),
                'floor': (0.0, -0.55, LADDER_TOP)},
    'SideExit': {'route': ((0.0, ROOT_OUT, SIDE_FLOOR - 0.15), ((0.0, ROOT_OUT, SIDE_FLOOR), 0.3), (0.95, 0.25, SIDE_FLOOR)),
                 'floor': (0.95, 0.25, SIDE_FLOOR)},
    'BottomStepOff': {'route': ((0.0, ROOT_OUT, 0.45), ((0.0, ROOT_OUT, 0.0), 0.5), (0.0, ROOT_OUT + 0.45, 0.0)),
                      'floor': (0.0, ROOT_OUT + 0.45, 0.0)},
    'TopEntry': {'route': ((0.0, -0.55, LADDER_TOP), ((0.0, ROOT_OUT, LADDER_TOP), 0.35), (0.0, ROOT_OUT, LADDER_TOP - 1.3)),
                 'floor': (0.0, -0.55, LADDER_TOP)},
}


def route_point(route, progress):
    start, corner, end = route
    t = smooth01(progress)
    if corner is None:
        return _lerp3(start, end, t)
    point, at = corner
    return _lerp3(start, point, t / at) if t < at else _lerp3(point, end, (t - at) / (1.0 - at))


# ---------------------------------------------------------------------------------- LimbGait port
class Gait:
    """Enough of LimbGait to place a loop's contacts: rung schedule, swing and body curve, no blending."""

    def __init__(self, shape, down):
        self.shape = shape
        self.loop = shape['down'] if down else shape['up']
        self.down = down
        self.cycle = shape['up']['strideRungs'] * PITCH
        self.base_rung = []
        lp = self.loop
        for limb in range(4):
            hand = limb < 2
            planted_range = (1.0 - lp['length'][limb]) * self.cycle
            low_rel = (SHOULDER_REL + shape['hand_mid_from_shoulder'] - planted_range * 0.5) if hand else shape['foot_low_rel']
            phase = lp['start'][limb] + lp['length'][limb] if down else lp['start'][limb]
            exact = (self.cycle * phase + low_rel) / PITCH
            rung = math.floor(exact + 1e-4) if hand else _round_half_even(exact)
            self.base_rung.append(int(rung) - (lp['strideRungs'] if down else 0))
        self.shift = 0
        if down:
            # In game a descent starts with a reversal, and LimbGait.MatchPhase shifts the down loop by whole rungs
            # (-stride..stride-1) to the contacts the climber already holds. The preview takes the shift that best
            # matches the ascent's stance over a cycle, so the descent is shown where the game shows it.
            ascent = Gait(shape, False)
            stride = lp['strideRungs']
            feet = [RUNG_BASE + self.cycle * (LOOP_CYCLES + i / 24.0) for i in range(24)]
            costs = []
            for k in range(-stride, stride):
                self.shift = k
                costs.append((sum(abs(self.target(l, f)[0] - ascent.target(l, f)[0]) for f in feet for l in range(4)), k))
            self.shift = min(costs)[1]

    def phase(self, feet):
        u = (feet - RUNG_BASE) / self.cycle
        return u - math.floor(u)

    def target(self, limb, feet, planted=False):
        """(rung height, swing progress 0..1 or 0 when planted, rung from, rung to)."""
        lp = self.loop
        u = (feet - RUNG_BASE) / self.cycle - lp['start'][limb]
        cycle = math.floor(u)
        w = max(0.0, min(1.0, (u - cycle) / lp['length'][limb]))
        if planted:
            w = 0.0 if w < 0.5 else 1.0
        start = self.base_rung[limb] + int(cycle) * lp['strideRungs'] + self.shift
        a, b = _clamp_rung(start), _clamp_rung(start + lp['strideRungs'])
        moving = a != b and 0.0 < w < 1.0
        height = RUNG_BASE + (a + (b - a) * sample(lp['swing'], w)) * PITCH
        return height, (w if moving else 0.0), a, b

    def body(self, feet):
        t = self.phase(feet)
        return sample(self.loop['body'], t, 0), sample(self.loop['body'], t, 1), sample(self.loop['lean'], t)


def _round_half_even(x):
    return int(round(x))  # Python rounds half to even, as Math.Round does


def _clamp_rung(rung):
    return max(0, min(RUNG_LAST, rung))


def rung_height(index):
    return RUNG_BASE + index * PITCH


# ------------------------------------------------------------------------------------------ preview
def _contact(limb, height):
    """World point of a planted palm / sole on a box rung at this rung-centre height."""
    hand = limb < 2
    side = 1.0 if limb in (0, 2) else -1.0
    span = min(RUNG_HALF_WIDTH - 0.04, HAND_SPAN if hand else FOOT_SPAN)
    return [side * span, HAND_OUT if hand else FOOT_OUT, height + (HAND_ABOVE if hand else FOOT_ABOVE)]


def _lerp3(a, b, t):
    return [a[i] + (b[i] - a[i]) * t for i in range(3)]


def lat_sign(limb):
    """Blender X of 'toward the limb's own side': +X for the left limbs, -X for the right."""
    return 1.0 if limb in (0, 2) else -1.0


def loop_time_to_feet(gait, time):
    """Feet height of the loop preview at time 0..1: phase = time climbing, 1 - time descending (the body moves
    down, so the descent plays the cycle backwards)."""
    return RUNG_BASE + gait.cycle * (LOOP_CYCLES + ((1.0 - time) if gait.down else time))


def loop_frame(shape, constants, down, time):
    """Pose of one loop preview frame. Returns dict: feet, phase, limbs[4] {height, swing, contact, arc, target},
    pelvis {base, offset(up, toward, lean)}."""
    gait = Gait(shape, down)
    feet = loop_time_to_feet(gait, time)
    root = [0.0, ROOT_OUT, feet]
    limbs = []
    for limb in range(4):
        height, swing, a, b = gait.target(limb, feet)
        contact = _contact(limb, height)
        arc = [sample(gait.loop['arc'][limb], swing, c) for c in range(3)] if swing > 0.0 else [0.0, 0.0, 0.0]
        target = [contact[0] + lat_sign(limb) * arc[0], contact[1] + arc[1], contact[2] + arc[2]]
        state = 'planted' if swing <= 0.0 else ('release' if swing < 0.15 else ('reach' if swing < 0.7 else 'approach'))
        limbs.append({'height': height, 'swing': swing, 'contact': contact, 'arc': arc, 'target': target,
                      'weight': 1.0, 'reach': 0.0, 'state': state, 'rungs': (a, b)})
    up, toward, lean = gait.body(feet)
    return {'feet': feet, 'phase': gait.phase(feet), 'root': root, 'limbs': limbs,
            'pelvis': _pelvis(root, constants, 1.0, up, toward, lean, (0.0, -1.0, 0.0))}


def _pelvis(root, constants, weight, up, toward, lean, toward_dir):
    """Climbing pelvis (presenter: feet + up*(PelvisHeight - drop + up) + toward*(PelvisToward + toward)), and the
    total lean in degrees. Weight is the body weight (0 = the game's own pose)."""
    base = [root[0], root[1], root[2] + PELVIS_HEIGHT - constants['PelvisDrop'] + up]
    reach = constants['PelvisToward'] + toward
    climbing = [base[0] + toward_dir[0] * reach, base[1] + toward_dir[1] * reach, base[2]]
    return {'climbing': climbing, 'lean': constants['Lean'] + lean, 'weight': weight, 'up': up, 'toward': toward,
            'extra_lean': lean}


def transition_frame(shape, constants, action, progress):
    """Pose of one transition preview frame at progress 0..1."""
    kind = action['kind']
    scenario = SCENARIOS[kind]
    start, _, end = scenario['route']
    floor = scenario['floor']
    root = route_point(scenario['route'], progress)
    # The ladder stance the limbs leave or join: a planted pose at the ladder end of the route.
    ladder_root = end if kind in ('GroundMount', 'AirCatch', 'TopEntry') else start
    gait = Gait(shape, kind == 'TopEntry')
    # The walking direction onto the floor (for a top entry: from the ladder back toward the floor it came from).
    travel = [floor[0] - ladder_root[0], floor[1] - ladder_root[1], 0.0] if floor else [0.0, -1.0, 0.0]
    norm = math.hypot(travel[0], travel[1]) or 1.0
    travel = [travel[0] / norm, travel[1] / norm, 0.0]
    left = [-travel[1], travel[0], 0.0]   # Z x travel
    limbs = []
    for limb in range(4):
        hand = limb < 2
        side = 1.0 if limb in (0, 2) else -1.0
        height, _, a, b = gait.target(limb, ladder_root[2], planted=True)
        contact = _contact(limb, height)
        reach = sample(action['reach'][limb], progress)
        weight = sample(action['weight'][limb], progress)
        if reach > 0.0 and floor:
            span = HAND_SPAN if hand else FOOT_SPAN
            dest = [floor[0] + left[0] * side * span, floor[1] + left[1] * side * span, floor[2]]
            if hand:
                dest = [dest[0] + travel[0] * EXIT_HAND_AHEAD, dest[1] + travel[1] * EXIT_HAND_AHEAD, dest[2] + PALM_THICKNESS]
            contact = _lerp3(contact, dest, reach)
        arc = [sample(action['arc'][limb], progress, c) for c in range(3)]
        target = [contact[0] + lat_sign(limb) * arc[0], contact[1] + arc[1], contact[2] + arc[2]]
        limbs.append({'height': height, 'swing': 0.0, 'contact': contact, 'arc': arc, 'target': target,
                      'weight': weight, 'reach': reach, 'state': 'authored', 'rungs': (a, b)})
    up, toward, lean = (sample(action['bodyUp'], progress), sample(action['bodyToward'], progress),
                        sample(action['lean'], progress))
    pelvis = _pelvis(root, constants, sample(action['bodyWeight'], progress), up, toward, lean, (0.0, -1.0, 0.0))
    return {'feet': root[2], 'phase': progress, 'root': root, 'limbs': limbs, 'pelvis': pelvis}


# ----------------------------------------------------------------------------------- comparison
def compare(expected, actual, path='', tolerance=1e-4, out=None):
    """Recursive numeric comparison; returns a list of 'path: expected vs actual' strings."""
    out = [] if out is None else out
    if isinstance(expected, dict):
        for key in expected:
            if key not in actual:
                out.append('%s.%s missing' % (path, key))
            else:
                compare(expected[key], actual[key], '%s.%s' % (path, key), tolerance, out)
    elif isinstance(expected, (list, tuple)):
        if len(expected) != len(actual):
            out.append('%s: length %d vs %d' % (path, len(expected), len(actual)))
        else:
            for i, (e, a) in enumerate(zip(expected, actual)):
                compare(e, a, '%s[%d]' % (path, i), tolerance, out)
    elif isinstance(expected, (int, float)) and not isinstance(expected, bool):
        if abs(float(expected) - float(actual)) > tolerance:
            out.append('%s: %.6g vs %.6g' % (path, expected, actual))
    elif expected != actual:
        out.append('%s: %r vs %r' % (path, expected, actual))
    return out
