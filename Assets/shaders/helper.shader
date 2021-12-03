Shader "MJD/helper"
{
    Properties
    {
        _Size("",float) = 0.1
        _MainTex("",2D) = ""{}  
    }
    SubShader
    {
        Pass
        {
            Name "Depth"  //0
            
            Cull Off

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma geometry geom

            #include "UnityCG.cginc"
            struct Particle
            {
                float4 pos,pos_last,vel,posDelta,other0,other1;
            };
            StructuredBuffer<Particle> particles;
            struct verin
            {
                float4 vertex : POSITION;
            };
            struct geoin
            {
                float4 posW:POSITION;
            };
            struct pixin
            {
                float3 centerV:TEXCOORD0;
                float3 posV   :TEXCOORD1;
                float4 posC  : SV_POSITION;
            };

            float _Size;
            float4x4 _MatrixM;
            int MELTING_SOLID_ID;
            
            geoin vert (uint vertex_id:SV_VertexID)
            {
                geoin o;
                o.posW =  mul(_MatrixM,particles[vertex_id].pos);
                if(asint(particles[vertex_id].other0.x)>MELTING_SOLID_ID) o.posW.w = 0.0f;
                return o;
            }

            [maxvertexcount(4)]
            void geom(point geoin v[1],inout TriangleStream<pixin> triStream)
            {
                pixin o[4];
                float3 posW        = v[0].posW.xyz/v[0].posW.w;
                float3 posV        = mul(UNITY_MATRIX_V,float4(posW,1.0f)).xyz;
                float3 normal      =  normalize(_WorldSpaceCameraPos - posW);
                float3 camereUpInW = UNITY_MATRIX_V[0].xyz;
                float3 a       = normalize(cross(normal,camereUpInW));
                float3 b       = cross(normal,a);

                float4 posw[4];
                posw[0] = float4(posW+(a-b)*_Size,1.0f);
                posw[1] = float4(posW+(a+b)*_Size,1.0f);
                posw[2] = float4(posW-(a+b)*_Size,1.0f);
                posw[3] = float4(posW-(a-b)*_Size,1.0f);
                [unroll]
                 for(int i=0;i<4;i++) 
                 {
                     o[i].centerV = posV;
                     o[i].posV    = mul(UNITY_MATRIX_V  ,posw[i]);
                     o[i].posC    = mul(UNITY_MATRIX_VP,posw[i]);
                     triStream.Append(o[i]);
                 }
            }

            fixed4 frag (pixin i) : SV_Target
            {
                float RSqr   = _Size*_Size;
                float3 x     = i.posV - i.centerV;
                float dstSqr = dot(x,x);
                if(dstSqr > RSqr) discard;
                float3 normal    = normalize(-normalize(i.centerV)*sqrt(RSqr-dstSqr)+x);
                float4 posC      = mul(UNITY_MATRIX_P,float4(normal*_Size + i.centerV,1.0f));
                float ndcZ       = posC.z/posC.w;
                return EncodeFloatRGBA(ndcZ);
            }
            ENDCG
        }
        Pass
        {
            Name "Thickness"   //1
            
            Cull Off
            ZTest Off
            Blend One One

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma geometry geom

            #include "UnityCG.cginc"
            struct Particle
            {
                float4 pos,pos_last,vel,posDelta,other0,other1;
            };
            StructuredBuffer<Particle> particles;

            struct verin
            {
                float4 vertex : POSITION;
            };
            struct geoin
            {
                float4 posW:POSITION;
            };
            struct pixin
            {
                float3 centerV:TEXCOORD0;
                float3 posV   :TEXCOORD1;
                float4 posC  : SV_POSITION;
            };

            float _Size;
            float _Thickness;
            float _ThickParticleRadiusSwellFactor;
            float4x4 _MatrixM;
            int MELTING_SOLID_ID;

            geoin vert (uint vertex_id:SV_VertexID)
            {
                geoin o;
                o.posW =  mul(_MatrixM,particles[vertex_id].pos);
                if(asint(particles[vertex_id].other0.x)>MELTING_SOLID_ID) o.posW.w = 0.0f;
                return o;
            }

            [maxvertexcount(4)]
            void geom(point geoin v[1],inout TriangleStream<pixin> triStream)
            {
                pixin o[4];
                float3 posW        = v[0].posW.xyz/v[0].posW.w;
                float3 posV        = mul(UNITY_MATRIX_V,float4(posW,1.0f)).xyz;
                float3 normal      =  normalize(_WorldSpaceCameraPos - posW);
                float3 camereUpInW = UNITY_MATRIX_V[0].xyz;
                float3 a       = normalize(cross(normal,camereUpInW));
                float3 b       = cross(normal,a);
                float halfSize = _Size *_ThickParticleRadiusSwellFactor;

                float4 posw[4];
                posw[0] = float4(posW+(a-b)*halfSize,1.0f);
                posw[1] = float4(posW+(a+b)*halfSize,1.0f);
                posw[2] = float4(posW-(a+b)*halfSize,1.0f);
                posw[3] = float4(posW-(a-b)*halfSize,1.0f);
                [unroll]
                 for(int i=0;i<4;i++) 
                 {
                     o[i].centerV = posV;
                     o[i].posV    = mul(UNITY_MATRIX_V  ,posw[i]);
                     o[i].posC    = mul(UNITY_MATRIX_VP,posw[i]);
                     triStream.Append(o[i]);
                 }
            }

            float4 frag (pixin i):SV_TARGET 
            {
                float RSqr   = _Size*_Size*_ThickParticleRadiusSwellFactor*_ThickParticleRadiusSwellFactor;
                float3 x     = i.posV - i.centerV;
                float dstSqr = dot(x,x);
                if(dstSqr > RSqr) discard;
                float3 normal    = normalize(-normalize(i.centerV)*sqrt(RSqr-dstSqr)+x);
                float c = _Thickness*(abs(normal.z));
                return float4(c,c,c,1.0f);
            }
            ENDCG
        }
        Pass
        {
            Name "Reconstruct normal" //2
            
            Cull Off

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "UnityCG.cginc"

            sampler2D _MainTex;

            float3 depthTex2ViewPos(float2 uv)
            {
                // ndc's x,y,z [0,1]
                float ndcZ = DecodeFloatRGBA(tex2D(_MainTex,uv));
                float4 clipPos = float4(uv*2.0f-1.0f,1.0-ndcZ,1.0f);
                float4 viewPos = mul(unity_CameraInvProjection,clipPos);
                return viewPos.xyz/viewPos.w;
            }

            struct verin{
                float4 vertex:POSITION;
                float2 uv:TEXCOORD0;
            };
            struct pixin{
                float4 posC:SV_POSITION;
                float2 uv:TEXCOORD0;
            };

            pixin vert (verin v)
            {
                pixin o;
                o.posC = UnityObjectToClipPos( v.vertex);
                o.uv   = v.uv;
                return o;
            }

            float3 reconstructNormal(float2 uv)
            {
                float _XStep = _ScreenParams.z-1.0f;
                float _YStep = _ScreenParams.w-1.0f;
                float3 posView = depthTex2ViewPos(uv);
                float3 ddxLeft = posView - depthTex2ViewPos(uv-float2(_XStep,0.0f));
                float3 ddxRight = depthTex2ViewPos(uv+float2(_XStep,0.0f)) - posView;
                float3 ddyDown = posView - depthTex2ViewPos(uv-float2(0.0f,_YStep));
                float3 ddyUp = depthTex2ViewPos(uv+float2(0.0f,_YStep)) - posView;
                float3 dx = ddxLeft;
                float3 dy = ddyUp;
                if(abs(ddxRight.z)<abs(ddxLeft.z)) dx=ddxRight;
                if(abs(ddyDown.z)<abs(ddyUp.z))    dy=ddyDown;
                return float4(normalize(cross(dx,dy)),1.0f);
            }

            float4 frag (pixin i):SV_TARGET 
            {
                if(DecodeFloatRGBA(tex2D(_MainTex,i.uv))<1e-7) return float4(0.0f,0.0f,0.0f,0.0f);
                return float4(reconstructNormal(i.uv),1.0f);
                // float _XStep = _ScreenParams.z-1.0f;
                // float _YStep = _ScreenParams.w-1.0f;
                // float3 posView = depthTex2ViewPos(i.uv);
                // float3 ddxLeft = posView - depthTex2ViewPos(i.uv-float2(_XStep,0.0f));
                // float3 ddxRight = depthTex2ViewPos(i.uv+float2(_XStep,0.0f)) - posView;
                // float3 ddyDown = posView - depthTex2ViewPos(i.uv-float2(0.0f,_YStep));
                // float3 ddyUp = depthTex2ViewPos(i.uv+float2(0.0f,_YStep)) - posView;
                // float3 dx = ddxLeft;
                // float3 dy = ddyUp;
                // if(abs(ddxRight.z)<abs(ddxLeft.z)) dx=ddxRight;
                // if(abs(ddyDown.z)<abs(ddyUp.z))    dy=ddyDown;
                // return float4(normalize(cross(dx,dy)),1.0f);
            }
            ENDCG
        }
        Pass
        {
            Name "bilinear blur"  // 3
            
            Cull Off

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float _FilterRadius;
            const float blurScale = 0.05f;
            const float blurDepthFalloff = 500.0f;

            struct verin{
                float4 vertex:POSITION;
                float2 uv:TEXCOORD0;
            };
            struct pixin{
                float4 posC:SV_POSITION;
                float2 uv:TEXCOORD0;
            };

            pixin vert (verin v)
            {
                pixin o;
                o.posC = UnityObjectToClipPos( v.vertex);
                o.uv   = v.uv;
                return o;
            }

            float4 frag (pixin i):SV_TARGET 
            {
                float value = DecodeFloatRGBA(tex2D(_MainTex,i.uv));
                float sum = 0.0f;
                float2 texOffset = float2(_ScreenParams.z-1.0f,_ScreenParams.w-1.0f);
                float wsum = 0.0f;
                for(float y = -_FilterRadius;y<=_FilterRadius;y+=1.0f)
                    for(float x = -_FilterRadius;x<=_FilterRadius;x+=1.0f)
                    {
                        float sample = DecodeFloatRGBA(tex2D(_MainTex,i.uv+float2(x,y)*texOffset));
                        float r = length(float2(x,y))*blurScale;
                        float w = exp(-r*r);
                        float r2 = (sample-value)*blurDepthFalloff;
                        float g = exp(-r2*r2);
                        sum+=sample*w*g;
                        wsum+=w*g;
                    }
                if(wsum>=0.0f) sum/=wsum;
                if(value < 1e-5) sum = value;
                return EncodeFloatRGBA( saturate(sum));
            }
            ENDCG
        }
        Pass
        {
            Name "decode texture"  // 4
            
            Cull Off

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "UnityCG.cginc"

            sampler2D _MainTex;

            struct verin{
                float4 vertex:POSITION;
                float2 uv:TEXCOORD0;
            };
            struct pixin{
                float4 posC:SV_POSITION;
                float2 uv:TEXCOORD0;
            };

            pixin vert (verin v)
            {
                pixin o;
                o.posC = UnityObjectToClipPos( v.vertex);
                o.uv   = v.uv;
                return o;
            }

            float4 frag (pixin i):SV_TARGET 
            {
                // return tex2D(_MainTex,i.uv);
                float value = DecodeFloatRGBA(tex2D(_MainTex,i.uv));
                return float4(value,value,value,1);
            }
            ENDCG
        }

        Pass
        {
            Name "sphere"  //5
            
            Cull Off

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma geometry geom

            #include "UnityCG.cginc"
            #include "UnityLightingCommon.cginc"

            float3 HSV2RGB(float3 c)
            {
                float4 K = float4(1.0, 2.0 / 3.0, 1.0 / 3.0, 3.0);
                float3 p = abs(frac(c.xxx + K.xyz) * 6.0 - K.www);
                return c.z * lerp(K.xxx, saturate(p - K.xxx), c.y);
            }

            struct Particle
            {
                float4 pos,pos_last,vel,posDelta,other0,other1;
            };
            StructuredBuffer<Particle> particles;
            struct geoin
            {
                float4 posW:POSITION;
                float t:TEXCOORD0;
            };
            struct pixin
            {
                float3 centerV:TEXCOORD0;
                float3 posV   :TEXCOORD1;
                float4 posC  : SV_POSITION;
                float t:TEXCOORD2;
            };

            float _Size;
            float4x4 _MatrixM;
            int MELTING_SOLID_ID;

            geoin vert (uint vertex_id:SV_VertexID)
            {
                geoin o;
                o.posW =  mul(_MatrixM,particles[vertex_id].pos);
                o.t =  particles[vertex_id].other1.x;
                if(asint(particles[vertex_id].other0.x)>MELTING_SOLID_ID)  o.posW.w = 0.0f;
                return o;
            }

            [maxvertexcount(4)]
            void geom(point geoin v[1],inout TriangleStream<pixin> triStream)
            {
                pixin o[4];
                float3 posW        = v[0].posW.xyz/v[0].posW.w;
                float3 posV        = mul(UNITY_MATRIX_V,float4(posW,1.0f)).xyz;
                float3 normal      =  normalize(_WorldSpaceCameraPos - posW);
                float3 camereUpInW = UNITY_MATRIX_V[0].xyz;
                float3 a       = normalize(cross(normal,camereUpInW));
                float3 b       = cross(normal,a);

                float4 posw[4];
                posw[0] = float4(posW+(a-b)*_Size,1.0f);
                posw[1] = float4(posW+(a+b)*_Size,1.0f);
                posw[2] = float4(posW-(a+b)*_Size,1.0f);
                posw[3] = float4(posW-(a-b)*_Size,1.0f);
                [unroll]
                 for(int i=0;i<4;i++) 
                 {
                     o[i].centerV = posV;
                     o[i].posV    = mul(UNITY_MATRIX_V  ,posw[i]);
                     o[i].posC    = mul(UNITY_MATRIX_VP,posw[i]);
                     o[i].t = v[0].t;
                     triStream.Append(o[i]);
                 }
            }

            fixed4 frag (pixin i) : SV_Target
            {
                float RSqr   = _Size*_Size;
                float3 x     = i.posV - i.centerV;
                float dstSqr = dot(x,x);
                if(dstSqr > RSqr) discard;
                float3 normal    = normalize(-normalize(i.centerV)*sqrt(RSqr-dstSqr)+x);
                float4 posC      = mul(UNITY_MATRIX_P,float4(normal*_Size + i.centerV,1.0f));
                float ndcZ       = posC.z/posC.w;

                float3 posView = i.posV;
                float3 normalView = normal;

                float3 viewDir = -normalize(posView);
                float3 lightPosView = mul(UNITY_MATRIX_V,_WorldSpaceLightPos0).xyz;
                float3 lightDirView = normalize(lightPosView-posView);
                float3 halfway = normalize(viewDir+lightDirView);
                float3 specular = (_LightColor0  * pow(max(dot(halfway, normalView), 0.0f), 400.0f));
                float3 diffuse = HSV2RGB(float3(0.5-0.5*i.t*0.01,1,1)) * max(dot(lightDirView, normalView), 0.0f) * _LightColor0  ;
                
                return float4(specular+diffuse,1.0f);
            }
            ENDCG
        }

        Pass
        {
            Name "temperature"  //6
            
            Cull Off

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma geometry geom

            #include "UnityCG.cginc"
            #include "UnityLightingCommon.cginc"

            float3 HSV2RGB(float3 c)
            {
                float4 K = float4(1.0f, 2.0f / 3.0f, 1.0f / 3.0f, 3.0f);
                float3 p = abs(frac(c.xxx + K.xyz) * 6.0f - K.www);
                return c.z * lerp(K.xxx, saturate(p - K.xxx), c.y);
            }

            struct Particle
            {
                float4 pos,pos_last,vel,posDelta,other0,other1;
            };
            StructuredBuffer<Particle> particles;
            struct geoin
            {
                float4 posW:POSITION;
                float t:TEXCOORD0;
                float id:TEXCOORD3;
            };
            struct pixin
            {
                float3 centerV:TEXCOORD0;
                float3 posV   :TEXCOORD1;
                float4 posC  : SV_POSITION;
                float t:TEXCOORD2;
                float id:TEXCOORD3;
            };

            float _Size;
            float4x4 _MatrixM;
            int MELTING_SOLID_ID;

            geoin vert (uint vertex_id:SV_VertexID)
            {
                geoin o;
                o.posW =  mul(_MatrixM,particles[vertex_id].pos);
                o.t =  particles[vertex_id].other1.x;
                o.id = 0;
                if(asint(particles[vertex_id].other0.x)>MELTING_SOLID_ID) 
                    o.id = 100;
                return o;
            }

            [maxvertexcount(4)]
            void geom(point geoin v[1],inout TriangleStream<pixin> triStream)
            {
                pixin o[4];
                float3 posW        = v[0].posW.xyz;
                float3 posV        = mul(UNITY_MATRIX_V,float4(posW,1.0f)).xyz;
                float3 normal      =  normalize(_WorldSpaceCameraPos - posW);
                float3 camereUpInW = UNITY_MATRIX_V[0].xyz;
                float3 a       = normalize(cross(normal,camereUpInW));
                float3 b       = cross(normal,a);

                float4 posw[4];
                posw[0] = float4(posW+(a-b)*_Size,1.0f);
                posw[1] = float4(posW+(a+b)*_Size,1.0f);
                posw[2] = float4(posW-(a+b)*_Size,1.0f);
                posw[3] = float4(posW-(a-b)*_Size,1.0f);
                [unroll]
                 for(int i=0;i<4;i++) 
                 {
                     o[i].centerV = posV;
                     o[i].posV    = mul(UNITY_MATRIX_V  ,posw[i]);
                     o[i].posC    = mul(UNITY_MATRIX_VP,posw[i]);
                     o[i].t = v[0].t;
                     o[i].id = v[0].id;
                     triStream.Append(o[i]);
                 }
            }

            fixed4 frag (pixin i) : SV_Target
            {
                if(i.id>2) discard;
                float RSqr   = _Size*_Size;
                float3 x     = i.posV - i.centerV;
                float dstSqr = dot(x,x);
                if(dstSqr > RSqr) discard;
                return float4(HSV2RGB(float3(0.5-0.5*i.t*0.01,1,1)),0.5f);
            }
            ENDCG
        }
        Pass
        {
            Name "gaussian blur"  // 7
            
            Cull Off

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "UnityCG.cginc"

            float horizontal;
            sampler2D _MainTex;
            const float _Weight[8] ;
            // const float weight[8] = {1.0f/7.0f, 1.0f/7.0f, 1.0f/7.0f, 1.0f/7.0f, 1.0f/7.0f, 1.0f/7.0f, 1.0f/7.0f, 1.0f/7.0f};

            struct verin{
                float4 vertex:POSITION;
                float2 uv:TEXCOORD0;
            };
            struct pixin{
                float4 posC:SV_POSITION;
                float2 uv:TEXCOORD0;
            };

            pixin vert (verin v)
            {
                pixin o;
                o.posC = UnityObjectToClipPos( v.vertex);
                o.uv   = v.uv;
                return o;
            }

            float4 frag (pixin i):SV_TARGET 
            {
                float2 texOffset = float2(_ScreenParams.z-1.0f,_ScreenParams.w-1.0f);
                float3 sample = (tex2D(_MainTex,i.uv).xyz);
                float3 result = sample * _Weight[0];
                [unroll]
                for(int j = 1;j<=7;j++)
                {
                    sample = tex2D(_MainTex, i.uv + float2(horizontal,1.0f-horizontal)*j*texOffset).xyz;
                    result += sample * _Weight[j];
                    sample = tex2D(_MainTex, i.uv - float2(horizontal,1.0f-horizontal)*j*texOffset).xyz;
                    result += sample * _Weight[j];
                }
                return float4(result,0.5f);
            }
            ENDCG
        }
    }
}