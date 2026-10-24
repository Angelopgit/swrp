//=========================================================================================================================
// SWRP deflector shield (droideka bubble, Code/Npc/DroidekaShield.cs): additive, unlit. A bright fresnel rim, a faint hex
// lattice, slow energy bands crawling over it, and a ripple from the last hit (per-object attributes ShieldHitPos /
// ShieldHitTime / ShieldPower). Colour comes from the renderer tint.
//=========================================================================================================================
HEADER
{
	Description = "SWRP deflector shield";
	Version = 1;
}

FEATURES
{
	#include "common/features.hlsl"
}

MODES
{
	Forward();
}

COMMON
{
	#define S_TRANSLUCENT 1
	#define BLEND_MODE_ALREADY_SET 1
	#include "common/shared.hlsl"
}

struct VertexInput
{
	#include "common/vertexinput.hlsl"
};

struct PixelInput
{
	#include "common/pixelinput.hlsl"
};

VS
{
	#include "common/vertex.hlsl"

	PixelInput MainVs( VertexInput i )
	{
		PixelInput o = ProcessVertex( i );
		return FinalizeVertex( o );
	}
}

PS
{
	#define DEPTH_STATE_ALREADY_SET 1
	#include "common/pixel.hlsl"

	RenderState( BlendEnable, true );
	RenderState( SrcBlend, ONE );
	RenderState( DstBlend, ONE );
	RenderState( DepthWriteEnable, false );
	RenderState( DepthEnable, true );
	RenderState( CullMode, NONE );

	float3 g_vShieldHitPos < Attribute( "ShieldHitPos" ); Default3( 0.0, 0.0, -100000.0 ); >;
	float g_flShieldHitTime < Attribute( "ShieldHitTime" ); Default( -100.0 ); >;
	float g_flShieldPower < Attribute( "ShieldPower" ); Default( 1.0 ); >;

	// distance to the nearest hex cell's edge (0 at the centre, 0.5 on the edge)
	float HexDist( float2 p )
	{
		const float2 s = float2( 1.0, 1.7320508 );
		float4 hc = floor( float4( p, p - float2( 0.5, 1.0 ) ) / s.xyxy ) + 0.5;
		float4 h = float4( p - hc.xy * s, p - ( hc.zw + 0.5 ) * s );
		float2 g = dot( h.xy, h.xy ) < dot( h.zw, h.zw ) ? h.xy : h.zw;
		g = abs( g );
		return max( g.x * 0.8660254 + g.y * 0.5, g.y );
	}

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		float3 posWs = i.vPositionWithOffsetWs.xyz + g_vCameraPositionWs.xyz;
		float3 V = normalize( -i.vPositionWithOffsetWs.xyz );
		float3 N = normalize( i.vNormalWs.xyz );
		float ndv = abs( dot( N, V ) );
		float rim = pow( 1.0 - ndv, 3.0 );

		float hex = smoothstep( 0.40, 0.5, HexDist( i.vTextureCoords.xy * float2( 40.0, 22.0 ) ) );
		float bands = pow( 0.5 + 0.5 * sin( posWs.z * 0.12 - g_flTime * 2.6 + sin( posWs.x * 0.05 + g_flTime ) * 1.5 ), 6.0 );

		float t = g_flTime - g_flShieldHitTime;
		float d = distance( posWs, g_vShieldHitPos );
		float ring = exp( -pow( ( d - t * 170.0 ) / 7.0, 2.0 ) ) * saturate( 1.0 - t / 0.7 );
		float flash = exp( -d / 14.0 ) * saturate( 1.0 - t / 0.3 );

		float a = 0.035 + rim * 0.95 + hex * ( 0.06 + rim * 0.5 ) + bands * 0.07 + ring * ( 0.8 + hex ) + flash * 1.6;
		a *= g_flShieldPower;
		float3 col = i.vVertexColor.rgb * a + float3( 0.6, 0.8, 1.0 ) * flash * g_flShieldPower * 0.6;
		return float4( col, 1.0 );
	}
}
