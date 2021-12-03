Shader "MJD/particleSurfaceShading"
{
    Properties
    {
        _MainTex("",2D) = ""{}  // depth texture
    }
    SubShader
    {
        // GrabPass{"_BackgroundTex"}
        Pass
        {
            Name "Render surface"
            Cull Off ZWrite Off ZTest Always
            
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "UnityCG.cginc"
            #include "UnityLightingCommon.cginc"


            sampler2D _MainTex;  // i.e. depth tex
            sampler2D _DepthTex;  // i.e. depth tex
            sampler2D _NormalTex;
            sampler2D _ColoreTex;
            sampler2D _ThicknessTex;
            sampler2D _BackgroundTex;
            sampler2D _CameraDepthTexture;
            float  _RefractIndex;

            float3 depthTex2ViewPos(float2 uv)
            {
                // float z = DecodeFloatRGBA( tex2D(_MainTex,uv));
                // _MainTex -> DepthTex
                float z = DecodeFloatRGBA( tex2D(_DepthTex,uv));
                float4 clipPos = float4(uv*2.0f-1.0f,1.0-z,1.0f);
                float4 viewPos = mul(unity_CameraInvProjection,clipPos);
                return viewPos.xyz/viewPos.w;
            }

            struct verin{
                float4 vertex:POSITION;
                float2 uv:TEXCOORD0;
            };
            struct pixin{
                float4 posC:SV_POSITION;
                float4 grabuv:TEXCOORD0;
                float2 uv :TEXCOORD1;
            };

            pixin vert (verin v)
            {
                pixin o;
                o.posC = UnityObjectToClipPos( v.vertex);
                o.grabuv   = ComputeScreenPos(o.posC);
                // o.grabuv   = ComputeGrabScreenPos(o.posC);
                o.uv = v.uv;
                return o;
            }

            float4 frag (pixin i):SV_TARGET 
            {
                float refDepth = ( tex2D(_CameraDepthTexture,i.uv).x);
                // _MainTex -> DepthTex
                float depth = DecodeFloatRGBA( tex2D(_DepthTex,i.uv));
                float d = depth*500  ;//Linear01Depth(1.0-refDepth);// 1.0-depth); 
                // _BackgroundTex -> _MainTex
                // if( depth<=refDepth) discard;// return tex2D(_MainTex, i.uv);
                // if( depth<=refDepth) return tex2Dproj(_BackgroundTex, i.grabuv);
                float4 color = tex2D(_ColoreTex,i.uv);
                float3 posView = depthTex2ViewPos(i.uv);
                float3 normalView = tex2D(_NormalTex,i.uv).xyz;
                float thickness = (tex2D(_ThicknessTex,i.uv).x);
                float3 transmission = float3(exp(-(1.0-color.x)*thickness),exp(-(1.0-color.y)*thickness),exp(-(1.0-color.z)*thickness));
                float2 refractCoord = i.grabuv.xy/i.grabuv.w+normalView.xy*_RefractIndex;
                // _BackgroundTex -> _MainTex
                float3 refractColor = tex2D(_MainTex,refractCoord)*transmission;
                float3 viewDir = -normalize(posView);
                float3 lightPosView = mul(UNITY_MATRIX_V,_WorldSpaceLightPos0).xyz;
                float3 lightDirView = normalize(lightPosView-posView);
                float3 halfway = normalize(viewDir+lightDirView);
                float3 specular = (_LightColor0  * pow(max(dot(halfway, normalView), 0.0f), 400.0f));
                float3 diffuse = color.xyz * max(dot(lightDirView, normalView), 0.0f) * _LightColor0  * color.w;
                // float3 diffuse = color.xyz * dot(lightDirView, normalView) * _LightColor0  * color.w;
                
                return float4(specular+diffuse+refractColor,1.0f);
                return float4(refractColor,1.0f);
                return float4(d,d,d,1.0f);
                return color;
                return float4(normalView,1.0f);
                return float4(diffuse,1.0f);
                return float4(specular,1.0f);
            }
            ENDCG
        }
          Pass
        {
            Name "Render surface sphere"
            Cull Off ZWrite Off ZTest Always
            
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "UnityCG.cginc"
            #include "UnityLightingCommon.cginc"


            sampler2D _MainTex;  // i.e. background tex
            sampler2D _ColoreTex;

            struct verin{
                float4 vertex:POSITION;
                float2 uv:TEXCOORD0;
            };
            struct pixin{
                float4 posC:SV_POSITION;
                float4 grabuv:TEXCOORD0;
                float2 uv :TEXCOORD1;
            };

            pixin vert (verin v)
            {
                pixin o;
                o.posC = UnityObjectToClipPos( v.vertex);
                o.uv = v.uv;
                return o;
            }

            float4 frag (pixin i):SV_TARGET 
            {
                float4 color = tex2D(_ColoreTex,i.uv);
                if(color.x ==0 &&color.y==0 &&color.z==0) return tex2D(_MainTex,i.uv);
                else return color;
            }
            ENDCG
        }
    }
}