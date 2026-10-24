//=========================================================================================================================
// SWRP Tatooine sky (Code/Map/Tatooine/TatooinePlanet.cs shows it while the camera is on the planet). Based on the engine's
// atmosphere_sky.shader: it follows the scene's directional light (the Tatooine sun the day/night cycle moves), so the
// sky turns orange at dusk and shows stars at night. Changes: the camera height is measured from the landing field
// (the planet sits at z -200000 in the scene), the air is dustier and warmer, and there are two suns, Tatoo I (the
// light) and the smaller, oranger Tatoo II trailing it along the same arc.
//=========================================================================================================================
HEADER
{
	Description = "SWRP Tatooine sky: the engine's atmosphere sky made dusty and warm, with Tatooine's two suns";
	Version = 1;
}

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
MODES
{
	Forward();
}

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
FEATURES
{
	#include "vr_common_features.fxc"
}

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
COMMON
{
	#include "system.fxc"
	#include "vr_common.fxc"

	static const float kAtmosPi = 3.141592f;
	static const int kAtmosPrimarySteps = 16;
	static const int kAtmosSecondarySteps = 2;

	float2 RaySphereIntersection( float3 origin, float3 direction, float radius )
	{
		float a = dot( direction, direction );
		float b = 2.0f * dot( direction, origin );
		float c = dot( origin, origin ) - ( radius * radius );
		float discriminant = ( b * b ) - 4.0f * a * c;

		if ( discriminant < 0.0f )
		{
			return float2( 1e5f, -1e5f );
		}

		float sqrtDiscriminant = sqrt( discriminant );
		float invDenominator = 0.5f / a;
		return float2(
			( -b - sqrtDiscriminant ) * invDenominator,
			( -b + sqrtDiscriminant ) * invDenominator
		);
	}

	float3 atmosphere( float3 viewDir, float3 rayOrigin, float3 sunDirection, float sunIntensity, float planetRadius, float atmosphereRadius, float3 rayleighCoefficients, float mieCoefficient, float rayleighScaleHeight, float mieScaleHeight, float miePreferredScatteringDirection )
	{
		sunDirection = normalize( sunDirection );
		viewDir = normalize( viewDir );

		float2 intersections = RaySphereIntersection( rayOrigin, viewDir, atmosphereRadius );
		if ( intersections.x > intersections.y )
		{
			return 0.0f;
		}

		intersections.y = min( intersections.y, RaySphereIntersection( rayOrigin, viewDir, planetRadius ).x );
		float primaryStepSize = ( intersections.y - intersections.x ) / float( kAtmosPrimarySteps );

		float rayleighOpticalDepth = 0.0f;
		float mieOpticalDepth = 0.0f;
		float3 accumulatedRayleigh = 0.0f;
		float3 accumulatedMie = 0.0f;

		float cosTheta = dot( viewDir, sunDirection );
		float cosThetaSquared = cosTheta * cosTheta;
		float g = miePreferredScatteringDirection;
		float gSquared = g * g;
		float rayleighPhase = ( 3.0f / ( 16.0f * kAtmosPi ) ) * ( 1.0f + cosThetaSquared );
		float miePhaseDenominator = pow( 1.0f + gSquared - 2.0f * cosTheta * g, 1.5f ) * ( 2.0f + gSquared );
		float miePhaseNumerator = ( 1.0f - gSquared ) * ( cosThetaSquared + 1.0f );
		float miePhase = ( 3.0f / ( 8.0f * kAtmosPi ) ) * ( miePhaseNumerator / miePhaseDenominator );

		float primaryTime = 0.0f;

		[loop]
		for ( int primaryStep = 0; primaryStep < kAtmosPrimarySteps; ++primaryStep )
		{
			float3 samplePosition = rayOrigin + viewDir * ( primaryTime + primaryStepSize * 0.5f );
			float sampleHeight = length( samplePosition ) - planetRadius;

			float rayleighStep = exp( -sampleHeight / rayleighScaleHeight ) * primaryStepSize;
			float mieStep = exp( -sampleHeight / mieScaleHeight ) * primaryStepSize;

			rayleighOpticalDepth += rayleighStep;
			mieOpticalDepth += mieStep;

			float secondaryStepSize = RaySphereIntersection( samplePosition, sunDirection, atmosphereRadius ).y / float( kAtmosSecondarySteps );
			float secondaryTime = 0.0f;
			float secondaryRayleighOpticalDepth = 0.0f;
			float secondaryMieOpticalDepth = 0.0f;

			[loop]
			for ( int secondaryStep = 0; secondaryStep < kAtmosSecondarySteps; ++secondaryStep )
			{
				float3 secondaryPosition = samplePosition + sunDirection * ( secondaryTime + secondaryStepSize * 0.5f );
				float secondaryHeight = length( secondaryPosition ) - planetRadius;

				secondaryRayleighOpticalDepth += exp( -secondaryHeight / rayleighScaleHeight ) * secondaryStepSize;
				secondaryMieOpticalDepth += exp( -secondaryHeight / mieScaleHeight ) * secondaryStepSize;

				secondaryTime += secondaryStepSize;
			}

			float3 transmittance = exp( -( mieCoefficient * ( mieOpticalDepth + secondaryMieOpticalDepth ) + rayleighCoefficients * ( rayleighOpticalDepth + secondaryRayleighOpticalDepth ) ) );

			accumulatedRayleigh += rayleighStep * transmittance;
			accumulatedMie += mieStep * transmittance;

			primaryTime += primaryStepSize;
		}

		float3 rayleighContribution = rayleighPhase * rayleighCoefficients * accumulatedRayleigh;
		float3 mieContribution = ( miePhase * mieCoefficient ) * accumulatedMie;

		return sunIntensity * ( rayleighContribution + mieContribution );
	}
}

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
struct VS_INPUT
{
	float4 vPositionOs : POSITION < Semantic( PosXyz ); >;
};

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
struct PS_INPUT
{
	float3 vPositionWs : TEXCOORD1;

	#if ( PROGRAM == VFX_PROGRAM_VS )
		float4 vPositionPs	: SV_Position;
	#endif
	#if ( PROGRAM == VFX_PROGRAM_PS )
		float4 vPositionSs : SV_Position;
	#endif
};

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
VS
{
	// Includes -----------------------------------------------------------------------------------------------------------------------------------------------
	#define IS_SPRITECARD 1
	#include "system.fxc"

	// Main ---------------------------------------------------------------------------------------------------------------------------------------------------
	PS_INPUT MainVs( const VS_INPUT i )
	{
		PS_INPUT o;
		float flSkyboxScale = g_flNearPlane + g_flFarPlane;
		float3 vPositionWs = g_vCameraPositionWs.xyz + i.vPositionOs.xyz * flSkyboxScale;

		o.vPositionPs = Position3WsToPs( vPositionWs );
		o.vPositionWs = vPositionWs;

		return o;
	}
}

//-------------------------------------------------------------------------------------------------------------------------------------------------------------
PS
{
	#include "vr_lighting.fxc"
	#include "volumetric_fog.fxc"
	#include "common/Shadow.hlsl"
	
	// Combos -------------------------------------------------------------------------------------------------------------------------------------------------

	// Render State -------------------------------------------------------------------------------------------------------------------------------------------
	RenderState( CullMode, NONE );
	RenderState( DepthWriteEnable, false );
	RenderState( DepthEnable, true );
	RenderState( DepthFunc, GREATER_EQUAL );
	
	// Attributes ---------------------------------------------------------------------------------------------------------------------------------------------
	BoolAttribute( sky, true );

	// Output -------------------------------------------------------------------------------------------------------------------------------------------------
	struct PS_OUTPUT
	{
		float4 vColor0 : SV_Target0;
	};

	// Constants ----------------------------------------------------------------------------------------------------------------------------------------------

	// Main ---------------------------------------------------------------------------------------------------------------------------------------------------

	float noise( in float3 x )
	{
		float3 p = floor(x);
		float3 f = frac(x);
		f = f*f*(3.0-2.0*f);
		float2 uv = (p.xy+float2(37.0,17.0) * p.z) + f.xy;
		float2 rg = g_tBlueNoise.Sample( g_sBilinearWrap, (uv + 0.5 )/256.0 ).xy;
		return lerp( rg.x, rg.y, f.z );
	}

	// the landing field's height in scene units (TatooinePlanet.Origin.z), and metres per inch
	static const float kGroundZ = -200000.0;
	static const float kMetres = 0.0254;

	float3 GetAtmosphere( float3 ray, float3 sunDirectionWs, float3 sunColor )
	{
		float fPlanetSize = 6371e3;
		float fAtmosphereSize = 100e3;
		float fHeight = max( 2.0, ( g_vCameraPositionWs.z - kGroundZ ) * kMetres );

		float3 color = atmosphere
		(
			ray.xzy,
			float3( 0, fPlanetSize + fHeight, 0 ),
			sunDirectionWs.xzy,
			42,
			fPlanetSize,
			fPlanetSize + fAtmosphereSize,
			float3( 5.5e-6, 13.0e-6, 22.4e-6 ),   // Earth-like: the deep blue overhead of a dry desert sky
			30e-6,                                 // a little dusty: one and a half times the Mie of a clear sky
			8e3,
			1.6e3,
			0.76
		);
		// suspended sand: a warm wash that grows towards the horizon
		float horizon = pow( 1.0 - saturate( ray.z ), 6.0 );
		color = lerp( color, color * float3( 1.12, 1.02, 0.88 ), horizon * 0.35 );
		return color * sunColor;
	}

	float Stars( in float3 vRay )
	{
		const float fStarScale = 0.3;
		float vStars = noise( vRay * ( g_vViewportSize.y * fStarScale ) * 0.75 );
		vStars += noise( vRay * ( g_vViewportSize.y * fStarScale ) * 0.5 );
		vStars += noise( vRay * ( g_vViewportSize.y * fStarScale ) * 0.25 );
		vStars += noise( vRay * ( g_vViewportSize.y * fStarScale ) * 0.1 );
		vStars = 1.0 - clamp( vStars, 0.0, 1.0 );
		vStars *= saturate( vRay.z * 100 );
		return vStars;
	}

	// a sun disc of the given angular radius (degrees) with a soft corona
	float3 SunDisc( float3 vRay, float3 dir, float3 color, float radiusDeg, float glow )
	{
		float c = dot( vRay, dir );
		float r = cos( radians( radiusDeg ) );
		float disc = smoothstep( r - 0.00002, r + 0.00002, c );
		float corona = pow( saturate( c ), 2200.0 ) * glow + pow( saturate( c ), 180.0 ) * glow * 0.06;
		return color * ( disc * 40.0 + corona ) * saturate( vRay.z * 30.0 + 0.3 );
	}

	PS_OUTPUT MainPs( PS_INPUT i )
	{
		PS_OUTPUT o;
		float3 vPositionWs = i.vPositionWs;
		float3 vRay = normalize( vPositionWs - g_vCameraPositionWs );

		float3 sunDirectionWs = -g_DirectionalLightDirection.xyz;
		float3 sunColor = g_DirectionalLightColor.rgb;
		float day = saturate( sunDirectionWs.z * 4.0 + 0.3 );

		// stars fade in as the sun goes down
		bool moonSky = sunColor.b > sunColor.r;
		float3 vColor = Stars( vRay ) * ( moonSky ? 1.0 : ( 1.0 - day ) ) * 0.8;

		const float sunDirLengthSq = dot( sunDirectionWs, sunDirectionWs );
		const float sunColorLengthSq = dot( sunColor, sunColor );
		if ( sunDirLengthSq > 0.0f && sunColorLengthSq > 0.0f )
		{
			vColor += GetAtmosphere( vRay, sunDirectionWs, sunColor );
			// Tatoo II trails Tatoo I by about 9 degrees along the sun's arc, a touch lower
			float3 up = float3( 0, 0, 1 );
			float3 along = normalize( cross( up, sunDirectionWs ) + float3( 0.0001, 0, 0 ) );
			float3 sun2 = normalize( sunDirectionWs * cos( radians( 9.0 ) ) - along * sin( radians( 9.0 ) ) - up * 0.02 );
			// at night the scene's light is the moonlight (blue): draw it as one pale moon, not two suns
			bool moonlit = sunColor.b > sunColor.r;
			if ( moonlit )
				vColor += SunDisc( vRay, sunDirectionWs, normalize( sunColor ) * 0.6, 0.9, 0.3 );
			else
			{
				vColor += SunDisc( vRay, sunDirectionWs, sunColor * float3( 1.0, 0.97, 0.9 ), 0.55, 1.0 );
				vColor += SunDisc( vRay, sun2, sunColor * float3( 1.0, 0.78, 0.52 ), 0.42, 0.8 );
			}
		}

		o.vColor0 = float4( vColor, 1.0 );
		o.vColor0.rgb = ApplyVolumetricFog( o.vColor0.rgb, i.vPositionWs, i.vPositionSs.xy );
		return o;
	}
}
