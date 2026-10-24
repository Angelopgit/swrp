"""
Minimal Source engine model reader (MDL v44-49 + VVD + DX90 VTX), enough to pull the static mesh of a prop out of the
Notorious map's addon: LOD0 triangles with positions, normals, UVs and a material per triangle (resolved through the
model's skins and $cdmaterials). No animation, no flexes. Pure Python, runs in Blender or plain Python.

    from source_mdl import load_model
    m = load_model( r"...\\models\\foo\\bar.mdl" )   # -> Model(materials=[...], tris=[(mat_index, (p0,p1,p2), (n0,n1,n2), (uv0,uv1,uv2)), ...])
"""
import os
import struct


class Model:
	def __init__( self ):
		self.materials = []        # list of material paths per skin-0 texture slot, e.g. "models/bananakin/foo/bar"
		self.tris = []             # (material index, positions, normals, uvs)
		self.cd = []
		self.textures = []
		self.skins = []


def _str( data, offset ):
	end = data.index( b"\x00", offset )
	return data[offset:end].decode( "latin-1" )


def load_model( mdl_path, skin=0 ):
	base = mdl_path[:-4]
	mdl = open( mdl_path, "rb" ).read()
	vvd = open( base + ".vvd", "rb" ).read()
	vtx = open( base + ".dx90.vtx", "rb" ).read()

	ident, version = struct.unpack_from( "<4si", mdl, 0 )
	if ident != b"IDST":
		raise ValueError( "not an MDL" )

	m = Model()
	numtextures, textureindex, numcd, cdindex, numskinref, numskinfam, skinindex, numbodyparts, bodypartindex = \
		struct.unpack_from( "<9i", mdl, 204 )

	for i in range( numtextures ):
		o = textureindex + i * 64
		nameoff = struct.unpack_from( "<i", mdl, o )[0]
		m.textures.append( _str( mdl, o + nameoff ).replace( "\\", "/" ) )
	for i in range( numcd ):
		off = struct.unpack_from( "<i", mdl, cdindex + i * 4 )[0]
		m.cd.append( _str( mdl, off ).replace( "\\", "/" ).strip( "/" ) )
	for f in range( numskinfam ):
		m.skins.append( list( struct.unpack_from( f"<{numskinref}h", mdl, skinindex + f * numskinref * 2 ) ) )
	family = m.skins[min( skin, len( m.skins ) - 1 )] if m.skins else list( range( numtextures ) )

	# ---- VVD: vertices (LOD0), with fixups applied
	_, _, _, numlods = struct.unpack_from( "<4sii i", vvd, 0 )[:4]
	numlodverts = struct.unpack_from( "<8i", vvd, 16 )
	numfixups, fixupstart, vertstart, _ = struct.unpack_from( "<4i", vvd, 48 )
	count = numlodverts[0]

	def vert( i ):
		o = vertstart + i * 48 + 16
		px, py, pz, nx, ny, nz, u, v = struct.unpack_from( "<8f", vvd, o )
		return ( px, py, pz ), ( nx, ny, nz ), ( u, v )

	if numfixups:
		order = []
		for f in range( numfixups ):
			lod, src, num = struct.unpack_from( "<3i", vvd, fixupstart + f * 12 )
			if lod >= 0:
				order.extend( range( src, src + num ) )
		verts = [vert( i ) for i in order]
	else:
		verts = [vert( i ) for i in range( count )]

	# ---- VTX + MDL bodyparts: triangles
	vtx_version, _, _, _, _, _, vtx_numlods, _, vtx_numbody, vtx_bodyoff = struct.unpack_from( "<iiHHiiiiii", vtx, 0 )
	sg_size = 33 if version >= 49 else 25

	for bp in range( numbodyparts ):
		bpo = bodypartindex + bp * 16
		_, nummodels, _, modelindex = struct.unpack_from( "<4i", mdl, bpo )
		vbpo = vtx_bodyoff + bp * 8
		vnummodels, vmodeloff = struct.unpack_from( "<2i", vtx, vbpo )

		# only the first model of each bodypart (the default bodygroup choice)
		for mi in range( min( 1, nummodels ) ):
			mo = bpo + modelindex + mi * 148
			nummeshes, meshindex, numvertices, vertexindex = struct.unpack_from( "<4i", mdl, mo + 72 )
			model_vert_base = vertexindex // 48

			vmo = vbpo + vmodeloff + mi * 8
			numlod, lodoff = struct.unpack_from( "<2i", vtx, vmo )
			vlo = vmo + lodoff          # LOD 0
			vnummeshes, vmeshoff = struct.unpack_from( "<2i", vtx, vlo )

			for me in range( nummeshes ):
				meo = mo + meshindex + me * 116
				material, _, _, vertexoffset = struct.unpack_from( "<4i", mdl, meo )
				vmeo = vlo + vmeshoff + me * 9
				numsg, sgoff = struct.unpack_from( "<2i", vtx, vmeo )

				for sg in range( numsg ):
					sgo = vmeo + sgoff + sg * sg_size
					nverts, vertoff, nidx, idxoff = struct.unpack_from( "<4i", vtx, sgo )
					sgverts = [struct.unpack_from( "<H", vtx, sgo + vertoff + k * 9 + 4 )[0] for k in range( nverts )]
					idx = struct.unpack_from( f"<{nidx}H", vtx, sgo + idxoff )
					tex = family[material] if material < len( family ) else material
					for t in range( 0, nidx - 2, 3 ):
						tri = []
						for k in ( idx[t], idx[t + 1], idx[t + 2] ):
							vi = model_vert_base + vertexoffset + sgverts[k]
							tri.append( verts[vi] if vi < len( verts ) else verts[-1] )
						m.tris.append( ( tex, tuple( p for p, _, _ in tri ), tuple( n for _, n, _ in tri ), tuple( uv for _, _, uv in tri ) ) )

	return m


def material_candidates( model, tex_index ):
	"""Possible VMT paths (lower case, no extension) for a texture slot, in $cdmaterials order."""
	name = model.textures[tex_index] if tex_index < len( model.textures ) else ""
	out = []
	for cd in model.cd or [""]:
		out.append( ( cd + "/" + name ).strip( "/" ).lower() )
	return out


def load_phy( path ):
	"""
	The collision shape from a model's .phy: a list of convex pieces, each a list of triangles in model units (inches).
	Reads the IVP compact surface ledges (one ledge per convex piece); points are stored in meters, Y-down.
	"""
	d = open( path, "rb" ).read()
	hsize, _, nsolids, _ = struct.unpack_from( "<iiii", d, 0 )
	off = hsize
	pieces = []
	for _ in range( nsolids ):
		ssize = struct.unpack_from( "<i", d, off )[0]
		surf = off + 4 + ( 28 if d[off + 4:off + 8] == b"VPHY" else 0 )
		points_start = surf + struct.unpack_from( "<i", d, surf + 32 )[0]
		p = surf + 48
		while p < points_start:
			c_point_offset, _, flags, ntri, _ = struct.unpack_from( "<iiIhh", d, p )
			if ntri <= 0:
				break
			base = p + c_point_offset
			points_start = min( points_start, base )
			if flags & 3:
				p += 16 + ntri * 16      # a ledge-tree hull around other pieces (it would fill archways), not a real piece
				continue
			tris = []
			for t in range( ntri ):
				to = p + 16 + t * 16
				tri = []
				for k in range( 3 ):
					i = struct.unpack_from( "<H", d, to + 4 + k * 4 )[0]
					x, y, z = struct.unpack_from( "<3f", d, base + i * 16 )
					tri.append( ( x / 0.0254, z / 0.0254, -y / 0.0254 ) )
				tris.append( tri )
			pieces.append( tris )
			p += 16 + ntri * 16
		off += 4 + ssize
	return pieces
