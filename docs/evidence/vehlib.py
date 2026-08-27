# Realistic-ish low-poly vehicle generator for project_kocasinan.
# One loft engine drives cars, vans and buses.  xf = 0.0 REAR .. 1.0 FRONT.
# Every model carries an ORIGINAL badge (chrome "K" disc). No real-world marks.
import bpy, bmesh, math

K = 16                              # verts per cross-section ring
J_GLASS_SIDE = {4, 5, 10, 11}       # quad bands forming the side glass
J_TOP        = {6, 7, 8, 9}         # roof / windshield / backlight bands
J_UNDER      = {0, 15}              # underbody

# ---------------- curves (PCHIP: smooth, no overshoot) ----------------
def pchip(xs, ys):
    n = len(xs)
    if n == 1: return lambda t: ys[0]
    h = [xs[i+1]-xs[i] for i in range(n-1)]
    d = [(ys[i+1]-ys[i])/h[i] if h[i] else 0.0 for i in range(n-1)]
    m = [0.0]*n
    m[0], m[-1] = d[0], d[-1]
    for i in range(1, n-1):
        if d[i-1]*d[i] <= 0: m[i] = 0.0
        else:
            w1, w2 = 2*h[i]+h[i-1], h[i]+2*h[i-1]
            m[i] = (w1+w2) / (w1/d[i-1] + w2/d[i])
    def f(t):
        if t <= xs[0]:  return ys[0]
        if t >= xs[-1]: return ys[-1]
        lo, hi = 0, n-1
        while hi-lo > 1:
            mid = (lo+hi)//2
            if xs[mid] <= t: lo = mid
            else: hi = mid
        hh = h[lo]; s = (t-xs[lo])/hh
        h00 = 2*s**3-3*s**2+1; h10 = s**3-2*s**2+s
        h01 = -2*s**3+3*s**2;  h11 = s**3-s**2
        return h00*ys[lo] + h10*hh*m[lo] + h01*ys[lo+1] + h11*hh*m[lo+1]
    return f

def C(curve):
    return pchip([p[0] for p in curve], [p[1] for p in curve])

def stations_for(spec, n):
    hard = set()
    for k in ("roof","sill","belt","wbelt","wtopf","wsillf"):
        for x, _ in spec.get(k) or []: hard.add(round(x, 4))
    for i in range(n+1): hard.add(round(i/n, 4))
    for rng in [spec.get("cabin"), spec.get("wind"), spec.get("back")] + list(spec.get("glass_extra") or []):
        if rng: hard.add(round(rng[0],4)); hard.add(round(rng[1],4))
    for px, pw in (spec.get("pillars") or []):
        hard.add(round(px-pw,4)); hard.add(round(px+pw,4))
    return sorted(hard)

def inrange(x, r): return r is not None and r[0] <= x <= r[1]

# ---------------- materials ----------------
MATS = {}
def mat(name, color, rough=0.4, metal=0.0, emit=None, es=3.0):
    if name in MATS: return MATS[name]
    m = bpy.data.materials.new(name); m.use_nodes = True
    b = m.node_tree.nodes["Principled BSDF"]
    b.inputs["Base Color"].default_value = (*color, 1.0)
    b.inputs["Roughness"].default_value = rough
    b.inputs["Metallic"].default_value = metal
    if emit is not None:
        b.inputs["Emission Color"].default_value = (*emit, 1.0)
        b.inputs["Emission Strength"].default_value = es
    MATS[name] = m
    return m

def PAL():
    return dict(
        glass = mat("V_GLASS",  (0.016, 0.024, 0.038), 0.05, 0.0),
        under = mat("V_UNDER",  (0.035, 0.035, 0.040), 0.88),
        tire  = mat("V_TIRE",   (0.026, 0.026, 0.030), 0.94),
        rim   = mat("V_RIM",    (0.86, 0.88, 0.92),   0.28, 0.55),
        hub   = mat("V_HUB",    (0.075, 0.075, 0.085), 0.45, 0.30),
        chrome= mat("V_CHROME", (0.90, 0.91, 0.94),   0.18, 0.45),
        trim  = mat("V_TRIM",   (0.048, 0.048, 0.056), 0.42, 0.25),
        head  = mat("V_HEAD",   (0.90, 0.93, 0.98),   0.06, 0.0, (0.80, 0.87, 1.00), 2.2),
        tail  = mat("V_TAIL",   (0.55, 0.03, 0.04),   0.14, 0.0, (0.95, 0.05, 0.05), 2.6),
        amber = mat("V_AMBER",  (0.85, 0.36, 0.02),   0.16, 0.0, (1.00, 0.42, 0.03), 1.8),
        plate = mat("V_PLATE",  (0.88, 0.88, 0.84),   0.55),
    )

# ---------------- primitives ----------------
def new_obj(name, verts, faces, coll):
    me = bpy.data.meshes.new(name)
    me.from_pydata(verts, [], faces); me.validate()
    ob = bpy.data.objects.new(name, me); coll.objects.link(ob)
    return ob

def box(cx, cy, cz, sx, sy, sz, coll, name, m=None):
    hx, hy, hz = sx/2, sy/2, sz/2
    v = [(cx-hx,cy-hy,cz-hz),(cx+hx,cy-hy,cz-hz),(cx+hx,cy+hy,cz-hz),(cx-hx,cy+hy,cz-hz),
         (cx-hx,cy-hy,cz+hz),(cx+hx,cy-hy,cz+hz),(cx+hx,cy+hy,cz+hz),(cx-hx,cy+hy,cz+hz)]
    f = [(0,1,2,3),(7,6,5,4),(0,4,5,1),(1,5,6,2),(2,6,7,3),(3,7,4,0)]
    ob = new_obj(name, v, f, coll)
    if m: ob.data.materials.append(m)
    return ob

def revolve(cx, cy, cz, profile, seg, coll, name, m=None, axis='Y'):
    """profile = [(offset_along_axis, radius), ...] -> closed solid of revolution."""
    v, f = [], []
    for (off, r) in profile:
        for i in range(seg):
            a = 2*math.pi*i/seg
            u, w = r*math.cos(a), r*math.sin(a)
            if   axis == 'Y': v.append((cx+u, cy+off, cz+w))
            elif axis == 'X': v.append((cx+off, cy+u, cz+w))
            else:             v.append((cx+u, cy+w, cz+off))
    n = len(profile)
    for sgm in range(n-1):
        a, b = sgm*seg, (sgm+1)*seg
        for i in range(seg):
            j = (i+1) % seg
            f.append((a+i, a+j, b+j, b+i))
    f.append(tuple(range(seg)))
    f.append(tuple(range(n*seg-1, (n-1)*seg-1, -1)))
    ob = new_obj(name, v, f, coll)
    if m: ob.data.materials.append(m)
    return ob

def revolve_ring(cx, cy, cz, loop, seg, coll, name, m=None, axis='Y'):
    """loop = CLOSED [(offset, radius), ...] revolved about `axis` -> hollow ring solid."""
    v, f = [], []
    for (off, r) in loop:
        for i in range(seg):
            a = 2*math.pi*i/seg
            u, w = r*math.cos(a), r*math.sin(a)
            if   axis == 'Y': v.append((cx+u, cy+off, cz+w))
            elif axis == 'X': v.append((cx+off, cy+u, cz+w))
            else:             v.append((cx+u, cy+w, cz+off))
    n = len(loop)
    for sgm in range(n):
        a, b = sgm*seg, ((sgm+1) % n)*seg
        for i in range(seg):
            j = (i+1) % seg
            f.append((a+i, a+j, b+j, b+i))
    ob = new_obj(name, v, f, coll)
    if m: ob.data.materials.append(m)
    return ob

def apply_mods(ob):
    bpy.ops.object.select_all(action='DESELECT')
    ob.select_set(True); bpy.context.view_layer.objects.active = ob
    for md in list(ob.modifiers):
        try: bpy.ops.object.modifier_apply(modifier=md.name)
        except Exception: ob.modifiers.remove(md)

# ---------------- body loft ----------------
def ring(y_sill, y_belt, y_top, z_sill, z_belt, z_top, ghz, roofw, boxy):
    zr = max(z_top - z_belt, 1e-4); zl = max(z_belt - z_sill, 1e-4)
    a, b = ghz
    lo = 0.16 if not boxy else 0.05          # how vertical the lower body is
    right = [
        (0.0,            z_sill),
        (y_sill*(0.86 if boxy else 0.80), z_sill),
        (y_sill,         z_sill + zl*lo),
        (y_belt*0.995,   z_belt - zl*0.10),
        (y_belt,         z_belt),
        (y_top,          z_belt + zr*a),
        (y_top*0.985,    z_top  - zr*b),
        (y_top*roofw,    z_top),
        (0.0,            z_top),
    ]
    return right + [(-y, z) for (y, z) in reversed(right[1:-1])]

def build_body(spec, coll, pal):
    nm, L = spec["name"], spec["length"]
    boxy = spec.get("kind", "car") != "car"
    fr = {k: C(spec[k]) for k in ("roof","sill","belt","wbelt")}
    fr["wtopf"]  = C(spec.get("wtopf")  or [(0,0.88),(1,0.88)])
    fr["wsillf"] = C(spec.get("wsillf") or [(0,0.90),(1,0.90)])
    ghz   = spec.get("ghz", (0.14, 0.15))
    roofw = spec.get("roofw", 0.66)

    st = stations_for(spec, spec.get("nsec", 30))
    verts = []
    for xf in st:
        x = (xf - 0.5) * L
        zb, zt = fr["sill"](xf), fr["roof"](xf)
        zm = min(max(fr["belt"](xf), zb + 0.02), zt - 0.02)
        wb = fr["wbelt"](xf)
        for (y, z) in ring(wb*fr["wsillf"](xf), wb, wb*fr["wtopf"](xf), zb, zm, zt, ghz, roofw, boxy):
            verts.append((x, y, z))

    M = len(st); faces, tag = [], []
    for i in range(M-1):
        a, b = i*K, (i+1)*K
        xm = (st[i] + st[i+1]) / 2.0
        for j in range(K):
            k = (j+1) % K
            faces.append((a+j, a+k, b+k, b+j)); tag.append((j, xm))

    # End caps are a RECESSED FRAMED PANEL, not a flat n-gon: outer ring -> small inset lip ->
    # fanned inner panel. `window` glazes the upper fan wedges, which is what gives a bus a real
    # windscreen sitting flush in the face instead of a slab bolted onto the nose.
    inset = spec.get("cap_inset", 0.93)
    cdep  = spec.get("cap_depth", 0.05)
    def add_cap(ring_start, outward, window):
        base = len(verts)
        pts = verts[ring_start:ring_start+K]
        cx = pts[0][0]; cz = sum(q[2] for q in pts) / K
        d = cdep * outward
        for (px, py, pz) in pts:
            verts.append((px - d, py*inset, cz + (pz-cz)*inset))
        verts.append((cx - d, 0.0, cz))
        ctr = base + K
        for j in range(K):
            k = (j+1) % K
            faces.append((ring_start+j, ring_start+k, base+k, base+j)); tag.append(('C', 0))
        for j in range(K):
            k = (j+1) % K
            faces.append((base+j, base+k, ctr))
            tag.append(('C', 1 if (window and j in window) else 0))
    add_cap((M-1)*K, +1.0, spec.get("front_window"))
    add_cap(0,       -1.0, spec.get("rear_window"))

    ob = new_obj(nm + "_Body", verts, faces, coll)
    body = mat("BODY_" + nm, spec["color"], rough=spec.get("rough", 0.24), metal=spec.get("metal", 0.25))
    for m in (body, pal["glass"], pal["under"]): ob.data.materials.append(m)

    cab, wind, back = spec["cabin"], spec.get("wind"), spec.get("back")
    extra = spec.get("glass_extra", [])
    pill = spec.get("pillars") or []
    def is_pillar(x): return any(abs(x - px) < pw for px, pw in pill)
    midx = []
    for p, t in zip(ob.data.polygons, tag):
        if t[0] == 'C':
            p.material_index = t[1]; midx.append(t[1]); continue
        j, xm = t
        if   j in J_UNDER: p.material_index = 2
        elif j in J_GLASS_SIDE and not is_pillar(xm) and (inrange(xm, cab) or any(inrange(xm, e) for e in extra)):
            p.material_index = 1
        elif j in J_TOP and (inrange(xm, wind) or inrange(xm, back)):
            p.material_index = 1
        midx.append(p.material_index)

    # --- wheel arches: shallow pocket booleans, one per wheel ---
    wr, tr = spec["wheel_r"], spec.get("track", 1.0)
    arch = spec.get("arch", 1.15)
    cutters = []
    for xf in spec["axles"]:
        wy = fr["wbelt"](xf) * tr
        for sgn in (1, -1):
            for dy in (spec.get("dual_rear_offsets", {}).get(xf) or [0.0]):
                c = revolve((xf-0.5)*L, sgn*(wy + dy + spec["wheel_w"]*0.10), wr,
                            [(-0.34, wr*arch), (0.34, wr*arch)], 22, coll, "CUT")
                c.rotation_euler = (0.0, 0.1963 + 0.07*len(cutters), 0.0)   # never coplanar with the loft
                cutters.append(c)
    n_before = len(ob.data.polygons)
    for c in cutters:
        md = ob.modifiers.new("cut", 'BOOLEAN'); md.operation = 'DIFFERENCE'
        md.object = c; md.solver = 'EXACT'
    apply_mods(ob)
    for c in cutters: bpy.data.objects.remove(c, do_unlink=True)
    if len(ob.data.polygons) < 0.35 * n_before:          # boolean ate the body -> keep it uncut
        print("  ! arch boolean collapsed on", nm, "- rebuilt without wheel wells")
        ob.data.clear_geometry()
        ob.data.from_pydata(verts, [], faces); ob.data.validate()
        for p, mi in zip(ob.data.polygons, midx): p.material_index = mi

    me = ob.data
    bm = bmesh.new(); bm.from_mesh(me)
    bmesh.ops.remove_doubles(bm, verts=bm.verts[:], dist=1e-5)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces[:])
    bm.to_mesh(me); bm.free(); me.update()
    return ob

# ---------------- wheel ----------------
def wheel(x, y, z, r, w, coll, nm, pal, seg=18):
    hw = w/2
    out = [revolve_ring(x, y, z,
        [(-hw*0.60, r*0.70), (-hw, r*0.82), (-hw*0.90, r*0.985), (-hw*0.32, r),
         ( hw*0.32, r), ( hw*0.90, r*0.985), ( hw, r*0.82), ( hw*0.60, r*0.70)],
        seg, coll, nm+"_Tire", pal["tire"])]
    out.append(revolve(x, y, z, [(-hw*0.55, r*0.66), (hw*0.62, r*0.66)], seg, coll, nm+"_RimBarrel", pal["rim"]))
    out.append(revolve(x, y, z, [( hw*0.52, r*0.685), (hw*0.64, r*0.685)], seg, coll, nm+"_RimFace", pal["rim"]))
    for i in range(5):                                   # dark voids read as 5 spokes
        v = box(0, 0, r*0.42, r*0.26, w*0.10, r*0.34, coll, nm+"_Void", pal["hub"])
        v.rotation_euler = (0, 2*math.pi*i/5 + 0.32, 0)
        v.location = (x, y + hw*0.66, z)
        out.append(v)
    out.append(revolve(x, y, z, [(hw*0.60, r*0.19), (hw*0.76, r*0.19)], 10, coll, nm+"_Hub", pal["chrome"]))
    return out

def arch_band(x, y, z, R, t, w, sgn, coll, nm, m, seg=13):
    """Dark trim ring around a wheel opening — swept rectangle over the top of the arch."""
    v, f = [], []
    a0, a1 = math.radians(4), math.radians(176)
    for i in range(seg+1):
        a = a0 + (a1-a0)*i/seg
        ca, sa = math.cos(a), math.sin(a)
        for (dr, dy) in ((0.0, 0.0), (t, 0.0), (t, w*sgn), (0.0, w*sgn)):
            v.append((x + (R+dr)*ca, y + dy, z + (R+dr)*sa))
    for i in range(seg):
        a4, b4 = i*4, (i+1)*4
        for k in range(4):
            kk = (k+1) % 4
            f.append((a4+k, a4+kk, b4+kk, b4+k))
    f.append((0, 1, 2, 3)); f.append((seg*4+3, seg*4+2, seg*4+1, seg*4))
    ob = new_obj(nm+"_Arch", v, f, coll)
    if m: ob.data.materials.append(m)
    return ob

# ---------------- badge ----------------
def badge(x, z, fwd, s, coll, nm, pal):
    out = []
    d = revolve(x, 0.0, z, [(-0.16*s*fwd, 0.98*s), (0.14*s*fwd, 0.98*s)], 16, coll,
                nm+"_BadgeDisc", pal["trim"], axis='X')
    out.append(d)
    stem = box(x + fwd*0.20*s, -0.36*s, z,          0.16*s, 0.20*s, 1.18*s, coll, nm+"_K1", pal["chrome"])
    up   = box(x + fwd*0.20*s,  0.12*s, z + 0.28*s, 0.16*s, 0.78*s, 0.20*s, coll, nm+"_K2", pal["chrome"])
    dn   = box(x + fwd*0.20*s,  0.12*s, z - 0.28*s, 0.16*s, 0.78*s, 0.20*s, coll, nm+"_K3", pal["chrome"])
    up.rotation_euler = (math.radians(-33*fwd), 0, 0)
    dn.rotation_euler = (math.radians( 33*fwd), 0, 0)
    out += [stem, up, dn]
    return out

# ---------------- assembly ----------------
def build(spec, coll=None, offset=(0.0, 0.0)):
    coll = coll or bpy.context.scene.collection
    pal = PAL()
    nm, L = spec["name"], spec["length"]
    kind = spec.get("kind", "car")
    fr = {k: C(spec[k]) for k in ("roof","sill","belt","wbelt")}
    body_m = mat("BODY_" + nm, spec["color"])
    objs = [build_body(spec, coll, pal)]

    wr, ww, tr = spec["wheel_r"], spec["wheel_w"], spec.get("track", 1.0)
    for xf in spec["axles"]:
        x = (xf - 0.5) * L
        wy = fr["wbelt"](xf) * tr
        for sgn in (1, -1):
            for dy in (spec.get("dual_rear_offsets", {}).get(xf) or [0.0]):
                objs += wheel(x, sgn*(wy + dy), wr, wr, ww, coll, nm, pal)

    # pickup bed
    if spec.get("bed"):
        b0, b1, bz, bh = spec["bed"]; t = 0.09
        xa, xb = (b0-0.5)*L, (b1-0.5)*L
        wy = fr["wbelt"]((b0+b1)/2) * (C(spec.get("wtopf") or [(0,0.88),(1,0.88)])((b0+b1)/2))
        for sgn in (1, -1):
            objs.append(box((xa+xb)/2, sgn*(wy-t/2), bz+bh/2, xb-xa, t, bh, coll, nm+"_BedSide", body_m))
        fw = fr["wbelt"](b0) * 1.94
        objs.append(box(xa+t/2, 0, bz+bh/2, t, fw, bh, coll, nm+"_Tailgate", body_m))
        objs.append(box(xb-t/2, 0, bz+bh*0.78, t, fw, bh*1.55, coll, nm+"_BedHead", body_m))

    # wheel-arch trim rings (also hide the boolean seam)
    wtf = C(spec.get("wtopf") or [(0,0.88),(1,0.88)])
    if spec.get("arch_trim", True):
        R = spec["wheel_r"] * spec.get("arch", 1.15)
        for xf in spec["axles"]:
            x = (xf-0.5)*L; wy = fr["wbelt"](xf)
            for sgn in (1, -1):
                for dy in (spec.get("dual_rear_offsets", {}).get(xf) or [0.0]):
                    objs.append(arch_band(x, sgn*(wy + dy - 0.02), spec["wheel_r"], R-0.015,
                                          0.075, 0.085, sgn, coll, nm, pal["trim"]))

    # bumpers: cars are body-coloured mouldings (the loft already IS the bumper) so they only get a
    # slim dark valance; vans/buses get the real separate bar.
    if spec.get("bumpers", True):
        mass = spec.get("bumper_mass", kind != "car")
        for sx, xf, tg in ((1.0, 0.985, "F"), (-1.0, 0.015, "R")):
            x = sx*(L*0.5 - 0.075); w = fr["wbelt"](xf)*C(spec.get("wsillf") or [(0,0.9),(1,0.9)])(xf)
            zb = fr["sill"](xf) + spec.get("bumper_z", 0.13)
            bh = spec.get("bumper_h", 0.22)
            if mass:
                objs.append(box(x, 0, zb, 0.13, w*2.02, bh, coll, nm+"_Bump"+tg, body_m))
            objs.append(box(x + sx*0.012, 0, zb - bh*(0.60 if mass else 0.10), 0.11,
                            w*(1.90 if mass else 1.96), bh*0.32, coll, nm+"_Val"+tg, pal["trim"]))

    # door handles
    for i, hx in enumerate(spec.get("handles") or []):
        x = (hx-0.5)*L; w = fr["wbelt"](hx)*(wtf(hx)*0.30 + 0.70)
        z = fr["belt"](hx) - spec.get("handle_drop", 0.09)
        for sgn in (1, -1):
            objs.append(box(x, sgn*(w+0.012), z, spec.get("handle_len", 0.17), 0.035, 0.045,
                            coll, nm+f"_Handle{i}", pal["chrome"]))

    # lights — offsets are ABSOLUTE metres from the nose, so a 12 m coach doesn't get 0.5 m of overhang
    cd = spec.get("cap_depth", 0.05)
    nose = L * 0.5
    lw, lh = spec.get("light_w", 0.44), spec.get("light_h", 0.15)
    if kind == "car":
        zf = fr["roof"](0.95) - spec.get("light_drop", 0.17)
        zr = fr["roof"](0.05) - spec.get("light_drop_r", spec.get("light_drop", 0.17))
    else:
        zf = fr["sill"](0.95) + spec.get("light_up", 0.42)
        zr = fr["sill"](0.05) + spec.get("light_up", 0.42)
    zf = spec.get("light_z", zf); zr = spec.get("light_z_r", zr)
    for sx, xf, z, m, tg in ((1, 0.985, zf, pal["head"], "Head"), (-1, 0.015, zr, pal["tail"], "Tail")):
        x = sx * (nose - 0.055); w = fr["wbelt"](xf)
        for sgn in (1, -1):
            objs.append(box(x - sx*0.025, sgn*w*0.60, z, 0.14, w*lw*1.14, lh*1.32, coll, nm+"_"+tg+"Bez", pal["trim"]))
            objs.append(box(x,            sgn*w*0.60, z, 0.12, w*lw,      lh,      coll, nm+"_"+tg,      m))
            objs.append(box(x - sx*0.008, sgn*w*(0.60+lw*0.56), z, 0.11, w*lw*0.24, lh*0.86,
                            coll, nm+"_"+tg+"Ind", pal["amber"] if tg == "Head" else pal["tail"]))
    LZ = zf

    # grille + plates + badges, all seated on the RECESSED nose panel
    gz = LZ - spec.get("grille_drop", 0.20)
    gw = fr["wbelt"](0.99)
    if spec.get("grille_h", 0.19) > 0:
        objs.append(box(nose - cd + 0.010, 0, gz, 0.10, gw*spec.get("grille_w", 0.88),
                        spec.get("grille_h", 0.19), coll, nm+"_Grille", pal["trim"]))
    pz = spec.get("plate_z", gz - spec.get("grille_h", 0.19)*0.5 - 0.10)
    objs.append(box( nose - cd + 0.015, 0, pz, 0.06, 0.44, 0.12, coll, nm+"_PlateF", pal["plate"]))
    objs.append(box(-nose + cd - 0.015, 0, spec.get("plate_z_r", fr["sill"](0.03) + 0.24),
                    0.06, 0.44, 0.12, coll, nm+"_PlateR", pal["plate"]))
    bs = spec.get("badge", 0.085)
    bzf = spec.get("badge_z", gz + spec.get("grille_h", 0.19)*0.5 + bs*1.05)
    bzr = spec.get("badge_z_r", spec.get("badge_z", fr["belt"](0.02) - 0.04))
    objs += badge( nose - cd + 0.020, bzf,  1.0, bs, coll, nm, pal)
    objs += badge(-nose + cd - 0.020, bzr, -1.0, bs, coll, nm, pal)

    # destination sign: a full-width band across the TOP of the screen (this is what stops a bus
    # front reading as a blank slab), with a lit display panel let into it.
    if spec.get("sign"):
        sw = fr["wbelt"](0.99) * 1.62
        sh = spec.get("sign_h", 0.30)
        sz = fr["roof"](0.99) - spec.get("sign_drop", 0.30)
        objs.append(box(nose - 0.030, 0, sz, 0.10, sw,      sh,      coll, nm+"_Sign",    pal["trim"]))
        objs.append(box(nose - 0.008, 0, sz, 0.07, sw*0.80, sh*0.52, coll, nm+"_SignLed", pal["amber"]))

    # mirrors
    if spec.get("mirrors", True):
        mx = spec.get("mirror_x", 0.63)
        x = (mx-0.5)*L; w = fr["wbelt"](mx); z = fr["belt"](mx) + spec.get("mirror_z", 0.0)
        for sgn in (1, -1):
            objs.append(box(x, sgn*(w+0.035), z, 0.05, 0.13, 0.04, coll, nm+"_MirArm", pal["trim"]))
            objs.append(box(x-0.015, sgn*(w+0.115), z+0.028, 0.075, 0.075, 0.070, coll, nm+"_Mirror", body_m))

    # bus/van side doors  ->  [(xf_centre, width_frac_of_L), ...]
    for i, (dx, dwf) in enumerate(spec.get("doors", [])):
        x = (dx-0.5)*L; w = fr["wbelt"](dx)
        z0, z1 = fr["sill"](dx) + spec.get("door_low", 0.22), fr["belt"](dx) + spec.get("door_high", 0.30)
        objs.append(box(x, w*0.995, (z0+z1)/2, dwf*L, 0.05, z1-z0, coll, nm+f"_Door{i}", pal["glass"]))

    # roof kit (bus AC pod / destination sign)
    if spec.get("roofpod"):
        rx, rl, rh = spec["roofpod"]
        objs.append(box((rx-0.5)*L, 0, fr["roof"](rx) + rh/2 - 0.02, rl*L, fr["wbelt"](rx)*1.3, rh,
                        coll, nm+"_RoofPod", pal["trim"]))

    bpy.ops.object.select_all(action='DESELECT')
    for o in objs: o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    bpy.ops.object.join()
    v = bpy.context.view_layer.objects.active
    v.name = nm; v.data.name = nm

    bv = v.modifiers.new("bevel", 'BEVEL')
    bv.width = spec.get("bevel", 0.012); bv.segments = 2
    bv.limit_method = 'ANGLE'; bv.angle_limit = math.radians(38)
    bv.harden_normals = True
    bpy.ops.object.shade_smooth()
    try: bpy.ops.object.shade_smooth_by_angle(angle=math.radians(34))
    except Exception:
        try: bpy.ops.object.shade_auto_smooth(angle=math.radians(34))
        except Exception: pass
    v.location = (offset[0], offset[1], 0.0)
    return v
