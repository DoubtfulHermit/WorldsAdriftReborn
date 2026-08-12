"""Unity TRS composition — pure maths, no UnityPy, no I/O.

WHY THIS EXISTS
---------------
The original extractor walked the Unity transform hierarchy adding up only
``m_LocalPosition``.  That is correct **only** if every node in the chain is
unrotated *and* unit-scale.  It is not: on every island bundle checked, the LOD0
grid-cell GameObject carries ``m_LocalScale = (4,4,4)``, so every terrain vertex
was landing at a quarter of its true offset from its cell origin.  See
``verify_extract.py`` check 1/2 for the measurement.

CONVENTIONS — and how each one was confirmed
--------------------------------------------
1. **Composition order.**  Unity's ``Transform.localToWorldMatrix`` satisfies

       world = parent.localToWorldMatrix * Matrix4x4.TRS(localPosition,
                                                         localRotation,
                                                         localScale)

   and ``Matrix4x4.TRS(t,q,s) == Translate(t) * Rotate(q) * Scale(s)``.
   A point is a **column** vector and is transformed as ``p' = M * p``.
   Confirmed structurally against the data: the island terrain is a 64 m grid
   (``verify_extract`` check 2 infers exactly 64.00 m between cell origins) while
   each cell mesh's own vertices span ``0 .. ~16.8`` in mesh-local units.  The
   grid only tiles without gaps if the cell's ``localScale = 4`` multiplies the
   mesh-local coordinates *before* the cell's translation is added — i.e. exactly
   ``T * S``, not ``S * T`` and not ``T`` alone.  16 * 4 == 64.

2. **Handedness.**  There is nothing to convert.  Unity's left-handedness is a
   statement about how its axes are drawn, not about the algebra: the quaternion
   we read out of the asset is Unity's own ``m_LocalRotation`` and the
   coordinates we emit are consumed as Unity/SpatialOS coordinates.  Reading and
   writing in the same frame means no mirror step, and inserting one would be the
   bug.

3. **Quaternion -> matrix.**  ``quat_to_mat3`` below is validated at import-check
   time (``_self_check``) against a literal transcription of UnityEngine's
   ``Quaternion.operator*(Quaternion, Vector3)`` in ``rotate_vec_unity``.  Those
   are two independent derivations, so agreement to 1e-12 rules out the
   transpose (``R^T`` corresponds to ``q^-1 * v * q`` and fails the check for any
   non-identity quaternion).  This matters because every bundle inspected so far
   has identity rotations, which cannot by themselves distinguish R from R^T.

Everything here is float64; Unity runs float32, so expect agreement to ~1e-4 m,
far below the metre-scale question being asked.
"""

Mat4 = tuple  # 16 floats, row-major: m[r*4+c]

IDENTITY4 = (1.0, 0.0, 0.0, 0.0,
             0.0, 1.0, 0.0, 0.0,
             0.0, 0.0, 1.0, 0.0,
             0.0, 0.0, 0.0, 1.0)


def quat_to_mat3(x, y, z, w):
    """Unity quaternion (x,y,z,w) -> 3x3 rotation, row-major 9-tuple.

    Rotates a COLUMN vector: v' = R * v, equivalent to q * v * q^-1.
    """
    n = (x * x + y * y + z * z + w * w) ** 0.5
    if n == 0.0:
        return (1.0, 0.0, 0.0, 0.0, 1.0, 0.0, 0.0, 0.0, 1.0)
    x, y, z, w = x / n, y / n, z / n, w / n
    xx, yy, zz = x * x, y * y, z * z
    xy, xz, yz = x * y, x * z, y * z
    wx, wy, wz = w * x, w * y, w * z
    return (1 - 2 * (yy + zz), 2 * (xy - wz),     2 * (xz + wy),
            2 * (xy + wz),     1 - 2 * (xx + zz), 2 * (yz - wx),
            2 * (xz - wy),     2 * (yz + wx),     1 - 2 * (xx + yy))


def rotate_vec_unity(q, v):
    """Literal transcription of UnityEngine.Quaternion.operator*(Quaternion, Vector3).

    Kept verbatim (including the pointless-looking temporaries) so that it is
    obviously *not* derived from quat_to_mat3.  It is the independent witness
    that quat_to_mat3 is not transposed.
    """
    qx, qy, qz, qw = q
    n1 = qx * 2.0
    n2 = qy * 2.0
    n3 = qz * 2.0
    n4 = qx * n1
    n5 = qy * n2
    n6 = qz * n3
    n7 = qx * n2
    n8 = qx * n3
    n9 = qy * n3
    n10 = qw * n1
    n11 = qw * n2
    n12 = qw * n3
    px, py, pz = v
    return ((1.0 - (n5 + n6)) * px + (n7 - n12) * py + (n8 + n11) * pz,
            (n7 + n12) * px + (1.0 - (n4 + n6)) * py + (n9 - n10) * pz,
            (n8 - n11) * px + (n9 + n10) * py + (1.0 - (n4 + n5)) * pz)


def trs(t, q, s):
    """Matrix4x4.TRS(t, q, s) = Translate(t) * Rotate(q) * Scale(s), row-major."""
    r = quat_to_mat3(*q)
    sx, sy, sz = s
    return (r[0] * sx, r[1] * sy, r[2] * sz, t[0],
            r[3] * sx, r[4] * sy, r[5] * sz, t[1],
            r[6] * sx, r[7] * sy, r[8] * sz, t[2],
            0.0, 0.0, 0.0, 1.0)


def mat_mul(a, b):
    """Row-major 4x4 product a*b (a applied after b, as in world = parent * local)."""
    out = [0.0] * 16
    for i in range(4):
        ai = i * 4
        a0, a1, a2, a3 = a[ai], a[ai + 1], a[ai + 2], a[ai + 3]
        for j in range(4):
            out[ai + j] = (a0 * b[j] + a1 * b[4 + j] + a2 * b[8 + j] + a3 * b[12 + j])
    return tuple(out)


def transform_point(m, p):
    """Affine point transform p' = M * (p,1)."""
    x, y, z = p[0], p[1], p[2]
    return (m[0] * x + m[1] * y + m[2] * z + m[3],
            m[4] * x + m[5] * y + m[6] * z + m[7],
            m[8] * x + m[9] * y + m[10] * z + m[11])


def transform_direction(m, v):
    """Rotate+scale a direction (no translation)."""
    x, y, z = v[0], v[1], v[2]
    return (m[0] * x + m[1] * y + m[2] * z,
            m[4] * x + m[5] * y + m[6] * z,
            m[8] * x + m[9] * y + m[10] * z)


def normal_matrix(m):
    """Inverse-transpose of the upper-left 3x3, for transforming normals.

    Under non-uniform scale a normal is NOT the plain direction transform.  The
    island cells are uniformly scaled (4,4,4) so this reduces to the rotation,
    but props are not guaranteed uniform and this module is shared.
    Falls back to the plain 3x3 if singular.
    """
    a = (m[0], m[1], m[2], m[4], m[5], m[6], m[8], m[9], m[10])
    c00 = a[4] * a[8] - a[5] * a[7]
    c01 = a[5] * a[6] - a[3] * a[8]
    c02 = a[3] * a[7] - a[4] * a[6]
    det = a[0] * c00 + a[1] * c01 + a[2] * c02
    if abs(det) < 1e-20:
        return a
    c10 = a[2] * a[7] - a[1] * a[8]
    c11 = a[0] * a[8] - a[2] * a[6]
    c12 = a[1] * a[6] - a[0] * a[7]
    c20 = a[1] * a[5] - a[2] * a[4]
    c21 = a[2] * a[3] - a[0] * a[5]
    c22 = a[0] * a[4] - a[1] * a[3]
    # inverse = adj/det ; transpose(inverse) = cofactor/det
    return (c00 / det, c01 / det, c02 / det,
            c10 / det, c11 / det, c12 / det,
            c20 / det, c21 / det, c22 / det)


def apply3(a, v):
    x, y, z = v[0], v[1], v[2]
    return (a[0] * x + a[1] * y + a[2] * z,
            a[3] * x + a[4] * y + a[5] * z,
            a[6] * x + a[7] * y + a[8] * z)


def normalize(v):
    n = (v[0] * v[0] + v[1] * v[1] + v[2] * v[2]) ** 0.5
    return (v[0] / n, v[1] / n, v[2] / n) if n > 1e-12 else (0.0, 1.0, 0.0)


# ---------------------------------------------------------------- self-check
def _self_check():
    """Run on import.  Cheap, deterministic, and it is the whole argument that
    the rotation convention is right — so it must not be skippable."""
    import math
    import random
    rng = random.Random(20260808)
    for _ in range(200):
        q = [rng.uniform(-1, 1) for _ in range(4)]
        n = math.sqrt(sum(c * c for c in q))
        q = tuple(c / n for c in q)
        v = tuple(rng.uniform(-50, 50) for _ in range(3))
        r = quat_to_mat3(*q)
        a = apply3(r, v)
        b = rotate_vec_unity(q, v)
        assert max(abs(a[i] - b[i]) for i in range(3)) < 1e-9, (q, v, a, b)
        # orthonormal, det +1
        det = (r[0] * (r[4] * r[8] - r[5] * r[7])
               - r[1] * (r[3] * r[8] - r[5] * r[6])
               + r[2] * (r[3] * r[7] - r[4] * r[6]))
        assert abs(det - 1.0) < 1e-9, det
    # TRS ordering: scale must act before translation
    m = trs((10.0, 0.0, 0.0), (0.0, 0.0, 0.0, 1.0), (4.0, 4.0, 4.0))
    assert transform_point(m, (1.0, 0.0, 0.0)) == (14.0, 0.0, 0.0)
    # nested composition: world = parent * child
    p = trs((0.0, 100.0, 0.0), (0.0, 0.0, 0.0, 1.0), (2.0, 2.0, 2.0))
    c = trs((1.0, 0.0, 0.0), (0.0, 0.0, 0.0, 1.0), (3.0, 3.0, 3.0))
    w = mat_mul(p, c)
    # child sits at parent-local x=1 -> world x=2 ; its own unit vertex scales 2*3
    assert transform_point(w, (1.0, 0.0, 0.0)) == (8.0, 100.0, 0.0)
    # 90 deg about +Y sends +Z to +X (Unity's left-handed convention)
    qy90 = (0.0, math.sin(math.radians(45)), 0.0, math.cos(math.radians(45)))
    rv = rotate_vec_unity(qy90, (0.0, 0.0, 1.0))
    assert abs(rv[0] - 1.0) < 1e-9 and abs(rv[2]) < 1e-9, rv


_self_check()
