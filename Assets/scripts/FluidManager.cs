using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Runtime.InteropServices;
using VoxelSystem;
using System.Linq;

public class FluidManager : MonoBehaviour
{
    [Range(0,1.0f)]
    public float particleRadius ;
    [Range(0.01f,1.0f)]
    public float thicknessFactor;
    [Range(1.0f,5.0f)]
    public float refractIndex ;
    float thickParticleRadiusSwellFactor;
    [Range(0.0f,20.0f)]
    public float filterRadius;
    public Color surfaceColor;
    RenderTexture rt_depth,  rt_thickness,  rt_normal , rt_depthBlurred,rt_thicknessBlurred,rt_surfaceColor,rt_surfaceColorBlurred,rt_normalBlurred;
    Material mat_particleSurface,    mat_helper ;
    // public Vector3Int initFluidScale;
    Vector3 vec3_boundaryMax,vec3_boundaryMin;
    public float rho0;
    float scl;
    const int MAXPARTICLENUM = 65536;
    int particleNum,maxNumInCell,maxNumNeighbor,hashTableSize;
    ComputeBuffer cb_particle,  cb_neighborIndices,  cb_neighborNum,  cb_indicesInCell,  cb_numInCell;
    int[] arrayNumInCell,arrayIndicesInCell,arrayNeighborNum,arrayNeighborIndices,arrayHash,arrayBounds;
    // float[] arrayBounds;
    ComputeShader cs_solver;
    int dispatchXNum,dispatchGridXNum, cskTest,cskPrologue,cskClearCell,cskFindNeighbors,cskComputeLambdas,cskComputeDeltaPos,cskApplyDeltaPos,cskAdvectAttribute,  cskEpilogue ,cskComputeBounds,cskConvertBoundary;
    // --------NOTE 更改 struct Particle 时， 同时修改compute shader 和 shader  
    struct Particle
    {
        public Vector4 pos,   vel,   pos_last,  pos_delta;
        public int id,transformMatrixIndex;//0 water 1 solid
        public float lambda, lastTemperature ,temperature;
        public float rho,pad0,pad;
    };
    FluidInitializer[] initFluids;
    Particle[] particles;
    public Transform[] transform_boundary;
    Matrix4x4[] matrix_transforms;
    int meltingSolidID;
    void Start()
    {
        meltingSolidID = 10000;
        //---------------- for rendering-----------------------------------------------------------------
        Camera.main.depthTextureMode = DepthTextureMode.Depth;

        thickParticleRadiusSwellFactor = 2.0f;
        float[] gaussBlurWeights = {0.197448f, 0.174697f, 0.120999f, 0.065602f, 0.02784f, 0.009246f, 0.002403f, 0.000489f};
        mat_helper          = new Material(Shader.Find("MJD/helper"));
        mat_particleSurface = new Material(Shader.Find("MJD/particleSurfaceShading"));
        mat_helper.SetFloat("_Thickness",thicknessFactor);
        mat_helper.SetFloat("_Size",particleRadius);
        mat_helper.SetFloat("_FilterRadius",filterRadius);
        mat_helper.SetFloatArray("_Weight",gaussBlurWeights);
        mat_helper.SetInt("MELTING_SOLID_ID", meltingSolidID);  
        mat_particleSurface.SetColor("_Color",surfaceColor);
        mat_particleSurface.SetFloat("_RefractIndex",refractIndex);

        rt_depth   = new RenderTexture(Camera.main.pixelWidth,Camera.main.pixelHeight,24);    rt_depth.Create();
        rt_normal  = new RenderTexture(Camera.main.pixelWidth,Camera.main.pixelHeight,24);    rt_normal.Create();
        rt_thickness     = new RenderTexture(Camera.main.pixelWidth,Camera.main.pixelHeight,24);  rt_thickness.Create();
        rt_depthBlurred  = new RenderTexture(Camera.main.pixelWidth,Camera.main.pixelHeight,24);  rt_depthBlurred.Create();
        rt_surfaceColor  = new RenderTexture(Camera.main.pixelWidth,Camera.main.pixelHeight,24);  rt_surfaceColor.Create();
        rt_thicknessBlurred  = new RenderTexture(Camera.main.pixelWidth,Camera.main.pixelHeight,24);  rt_thicknessBlurred.Create();
        rt_surfaceColorBlurred  = new RenderTexture(Camera.main.pixelWidth,Camera.main.pixelHeight,24);  rt_surfaceColorBlurred.Create();
        rt_normalBlurred  = new RenderTexture(Camera.main.pixelWidth,Camera.main.pixelHeight,24);  rt_normalBlurred.Create();
        
        //---------------- for rendering end-----------------------------------------------------------------
        maxNumInCell = 100;
        maxNumNeighbor = 40; 
        scl = Mathf.Pow( transform.localToWorldMatrix.determinant,1.0f/3.0f);
        float cellSize = 4.0f*particleRadius;  
        Vector3Int gridNum = new Vector3Int(61,61,61);
        dispatchGridXNum = Mathf.CeilToInt(gridNum.x*gridNum.y*gridNum.z/128.0f);
        matrix_transforms = new Matrix4x4[6];
        matrix_transforms[0] = Matrix4x4.identity;
        hashTableSize = gridNum.x*gridNum.y*gridNum.z;
        initFluids = GetComponentsInChildren<FluidInitializer>();

        //  <10000 fluid  |  10000 melting solid  |  >10000  solid boundary
        List<Particle> particlesList = new List<Particle>();
        float avgTemper = 0.0f;
        float initPosDelta = 3.2f;
        ComputeShader voxelizer = Resources.Load<ComputeShader>("mjd/Voxelizer");
        MeshFilter[] mfs = this.GetComponentsInChildren<MeshFilter>();
        foreach(MeshFilter mf in mfs)
        {
            FluidInitializer fi = mf.gameObject.GetComponent<FluidInitializer>();
            float t = fi.temperature;
            int id = fi.ID;
            Mesh mesh = (mf.mesh);
            Vector3[] poss = mesh.vertices;
            for(int i = 0;i<mesh.vertexCount;i++)  poss[i] = Vector3.Scale(poss[i],mf.transform.localScale);
            mesh.SetVertices(new List<Vector3>(poss));
            Vector3[] Pos = GPUVoxelizer.Voxelize(voxelizer,mesh,particleRadius*initPosDelta,true).GetVoxelPostions() ;
            for(int i =0;i<Pos.Length;i++)
            {
                //体素化数据的结果大小是2的幂。多余的位置统一是（0，0，0）
                if(Pos[i].x==0.0f&&Pos[i].y==0.0f&&Pos[i].z==0.0f) continue;
                Particle p = new Particle();
                p.vel = Vector4.zero;
                Pos[i] = mf.transform.localRotation*Pos[i]+mf.transform.localPosition;
                p.pos = ( new Vector4(Pos[i].x,Pos[i].y,Pos[i].z,1.0f));
                p.pos_last = p.pos;
                p.temperature = p.lastTemperature = t;
                p.id = id;
                p.transformMatrixIndex = 0; 
                particlesList.Add(p);
                avgTemper+=p.temperature;
            }
            Destroy(mf.gameObject);
        }

        // Solid boundary particles. 
        // Solids will be voxelized in fluid's model space.But voxelized solid particles are stored in solid's model space.
        for(int j = 0;j<transform_boundary.Length;j++)
        {
            Matrix4x4 solid2Fluid=transform.worldToLocalMatrix*transform_boundary[j].localToWorldMatrix;
            Matrix4x4 fluid2Solid=transform_boundary[j].worldToLocalMatrix*transform.localToWorldMatrix;
            float solid2FluidScale = Mathf.Pow(solid2Fluid.determinant,1.0f/3.0f);
            Mesh mesh = (Mesh)Instantiate(transform_boundary[j].GetComponent<MeshFilter>().mesh);
            Vector3[] poss = mesh.vertices;
            for(int i = 0;i<mesh.vertexCount;i++) 
                poss[i]*=solid2FluidScale;
            mesh.SetVertices(new List<Vector3>(poss));
            Vector3[] solidPos = GPUVoxelizer.Voxelize(voxelizer,mesh,particleRadius*initPosDelta,false).GetVoxelPostions() ;
            for(int i =0;i<solidPos.Length;i++)
            {
                if(solidPos[i].x==0.0f&&solidPos[i].y==0.0f&&solidPos[i].z==0.0f) continue;
                Particle p = new Particle();
                solidPos[i]*=((1.0f/solid2FluidScale));
                solidPos[i] = solid2Fluid*(new Vector4(solidPos[i].x,solidPos[i].y,solidPos[i].z,1.0f));
                p.pos = fluid2Solid*(new Vector4(solidPos[i].x,solidPos[i].y,solidPos[i].z,1.0f));
                p.pos_last = p.pos;
                p.id = 10001;
                p.transformMatrixIndex = 1+j;
                particlesList.Add(p);
            }
        }

        vec3_boundaryMax = new Vector3(6.0f,6.0f,15.0f);vec3_boundaryMin = -vec3_boundaryMax;//Vector3.zero; 
        // vec3_boundaryMax = new Vector3(20.0f,20.0f,20.0f);vec3_boundaryMin = -vec3_boundaryMax; 
        // vec3_boundaryMax = new Vector3(1000.0f,1000.0f,1000.0f);vec3_boundaryMin = -vec3_boundaryMax; 

        particles = particlesList.ToArray();
        particleNum = particles.Length;
        avgTemper/=particleNum;

        cs_solver          = Resources.Load<ComputeShader>("mjd/FluidSolver");
        cskConvertBoundary = cs_solver.FindKernel("convertBoundary");
        cskPrologue        = cs_solver.FindKernel("prologue");
        cskClearCell       = cs_solver.FindKernel("clearCell");
        cskFindNeighbors   = cs_solver.FindKernel("findNeighbors");
        cskComputeLambdas  = cs_solver.FindKernel("computeLambdas");
        cskComputeDeltaPos = cs_solver.FindKernel("computeDeltaPos");
        cskApplyDeltaPos   = cs_solver.FindKernel("applyDeltaPos");
        cskAdvectAttribute = cs_solver.FindKernel("advectAttribute");
        cskEpilogue        = cs_solver.FindKernel("epilogue");

        // cs_solver.SetVector("gravity",new Vector4(0.0f,-9.81f,0.0f,0.0f));  
        cs_solver.SetVector("boundaryMax",vec3_boundaryMax);  
        cs_solver.SetVector("boundaryMin",vec3_boundaryMin);  
        cs_solver.SetFloat("particleRadius",particleRadius);  
        cs_solver.SetFloat("cellSize",cellSize);  
        cs_solver.SetFloat("supprotRadius",particleRadius*4.0f);  
        cs_solver.SetFloat("neighborSearchRadius",particleRadius*4.0f);  
        cs_solver.SetFloat("meltingPoint",10.0f);  
        cs_solver.SetFloat("rho0",rho0);  
        cs_solver.SetFloat("tConductRate", 0.01f);  
        cs_solver.SetFloat("timeStep", 1.0f/300.0f);
        cs_solver.SetInt("maxNumInCell", maxNumInCell);  
        cs_solver.SetInt("maxNumNeighbor", maxNumNeighbor);  
        cs_solver.SetInt("hashTableSize",gridNum.x*gridNum.y*gridNum.z);
        cs_solver.SetInt("particleNum", particleNum);  
        cs_solver.SetInt("maxRhoIndex", 0);  
        cs_solver.SetInt("MELTING_SOLID_ID", meltingSolidID);  

        dispatchXNum         = Mathf.CeilToInt(particleNum/128.0f);
        arrayNumInCell       = new int[gridNum.x*gridNum.y*gridNum.z];
        arrayIndicesInCell   = new int[gridNum.x*gridNum.y*gridNum.z*maxNumInCell];
        arrayNeighborNum     = new int[particleNum];
        arrayNeighborIndices = new int[particleNum*maxNumNeighbor];

        cb_particle        = new ComputeBuffer(particleNum,Marshal.SizeOf<Particle>());
        cb_particle.SetData(particles);
        cb_neighborIndices = new ComputeBuffer(arrayNeighborIndices.Length,sizeof(int));
        cb_neighborNum     = new ComputeBuffer(arrayNeighborNum.Length,sizeof(int));
        cb_indicesInCell   = new ComputeBuffer(arrayIndicesInCell.Length,sizeof(int));
        cb_numInCell       = new ComputeBuffer(arrayNumInCell.Length,sizeof(int));

        cs_solver.SetBuffer(cskConvertBoundary,"particles",cb_particle);

        cs_solver.SetBuffer(cskClearCell,"numInCell",cb_numInCell);

        cs_solver.SetBuffer(cskPrologue,"particles",cb_particle);
        cs_solver.SetBuffer(cskPrologue,"numInCell",cb_numInCell);
        cs_solver.SetBuffer(cskPrologue,"indicesInCell",cb_indicesInCell);
        
        cs_solver.SetBuffer(cskFindNeighbors,"particles",cb_particle);
        cs_solver.SetBuffer(cskFindNeighbors,"numInCell",cb_numInCell);
        cs_solver.SetBuffer(cskFindNeighbors,"indicesInCell",cb_indicesInCell);
        cs_solver.SetBuffer(cskFindNeighbors,"nNum",cb_neighborNum);
        cs_solver.SetBuffer(cskFindNeighbors,"nIndices",cb_neighborIndices);

        cs_solver.SetBuffer(cskComputeLambdas,"particles",cb_particle);
        cs_solver.SetBuffer(cskComputeLambdas,"nNum",cb_neighborNum);
        cs_solver.SetBuffer(cskComputeLambdas,"indicesInCell",cb_indicesInCell);
        cs_solver.SetBuffer(cskComputeLambdas,"nIndices",cb_neighborIndices);

        cs_solver.SetBuffer(cskComputeDeltaPos,"particles",cb_particle);
        cs_solver.SetBuffer(cskComputeDeltaPos,"indicesInCell",cb_indicesInCell);
        cs_solver.SetBuffer(cskComputeDeltaPos,"nNum",cb_neighborNum);
        cs_solver.SetBuffer(cskComputeDeltaPos,"nIndices",cb_neighborIndices);

        cs_solver.SetBuffer(cskApplyDeltaPos,"particles",cb_particle);

        cs_solver.SetBuffer(cskAdvectAttribute,"particles",cb_particle);
        cs_solver.SetBuffer(cskAdvectAttribute,"nNum",cb_neighborNum);
        cs_solver.SetBuffer(cskAdvectAttribute,"nIndices",cb_neighborIndices);

        cs_solver.SetBuffer(cskEpilogue,"particles",cb_particle);

        mat_helper.SetBuffer("particles",cb_particle);

        if(particleNum>MAXPARTICLENUM) Debug.LogError("particles too many! (max is 65536)");
        Debug.Log("total particle number  "+particleNum);
    }

    public bool renderParticle,renderTemperature;
    uint frameCnt = 0;
    // public bool debugF, frameDebug,showInfo,preParticleIndfo,showRho,showNeiNum,showHash,showBounds,startJitter;
    void Update()
    {
        // if(debugF&&frameDebug) return; frameDebug=true; 

        // if(transform_boundary.Length>0)    transform_boundary[0].rotation=Quaternion.Euler(transform_boundary[0].rotation.eulerAngles - new Vector3(0.05f,0,0));
 
        cs_solver.SetVector("gravity",Quaternion.Inverse(transform.rotation)*new Vector4(0.0f,-9.81f,0.0f,0.0f));  
        for(int i = 0;i<transform_boundary.Length;i++) 
            matrix_transforms[i+1] = transform.worldToLocalMatrix*transform_boundary[i].localToWorldMatrix;
        cs_solver.SetMatrixArray("matrices",matrix_transforms);
        
        for(int ii = 0; ii<3;ii++)
        {
        cs_solver.SetFloat("timePassed",(frameCnt++)*1.0f/300.0f);

        cs_solver.Dispatch(cskConvertBoundary,dispatchXNum,1,1);
        cs_solver.Dispatch(cskClearCell,dispatchGridXNum,1,1);
        cs_solver.Dispatch(cskPrologue, dispatchXNum,1,1);
        cs_solver.Dispatch(cskFindNeighbors,dispatchXNum,1,1);
        cs_solver.Dispatch(cskAdvectAttribute,dispatchXNum,1,1);
        for(int i =0;i<3;i++)
        {
            cs_solver.Dispatch(cskComputeLambdas,dispatchXNum,1,1);
            cs_solver.Dispatch(cskComputeDeltaPos,dispatchXNum,1,1);
            cs_solver.Dispatch(cskApplyDeltaPos,dispatchXNum,1,1);
        }
        cs_solver.Dispatch(cskEpilogue,dispatchXNum,1,1);
        }
        
        // if(showInfo)
        // {
        //     Debug.Log(frameCnt+"----------------------------------------------------------------------");
        //     if(showHash)
        //     {
        //         // cb_hashTable.GetData(arrayHash);
        //         cb_indicesInCell.GetData(arrayIndicesInCell);
        //         cb_numInCell.GetData(arrayNumInCell);
        //         // Debug.Log("frameNum------------hash value int4 ------------------------------- ");
        //         // for(int i = 0;i<arrayHash.Length;i+=4)
        //             // if(arrayHash[i+3]!=-1)
        //                 // Debug.Log(frameCnt+"  "+arrayHash[i]+" "+arrayHash[i+1]+" "+arrayHash[i+2]+" "+arrayHash[i+3]+" ");
        //         for(int i = 0;i<arrayNumInCell.Length;i++)
        //         {
        //             if(arrayNumInCell[i]<=0) continue;
        //             var s = "    ";
        //             for(int j = 0;j<arrayNumInCell[i];j++)
        //                 s+="  "+arrayIndicesInCell[maxNumInCell*i+j].ToString();
        //             Debug.Log("cell idx , particle number "+i+"  "+arrayNumInCell[i]+"  indices "+s);
        //         }
        //     }
        //     // Debug.Log(frameCnt+" vel--pos- posDelta-lambda---rho-----neighborNum---------------------------------------------------------------------");7
        //     if(preParticleIndfo)
        //     {
        //         // Debug.Log(frameCnt+" particle info-------------------------------------------------  ");//+"  "+maxV.ToString("f6")+"  "+minV.ToString("f6")+"  ");
        //         float maxRho = 0.0f,minRho = 10000000.0f;
        //         cb_particle.GetData(particles);
        //         for(int i = 0;i<particleNum;i++) 
        //         {
        //                 // Debug.Log(i+"   "+particles[i].lambda.ToString("f12")+"   "+particles[i].pos_delta.ToString("f12"));//+"  "+maxV.ToString("f6")+"  "+minV.ToString("f6")+"  ");
        //                 Debug.Log(i+"   "+particles[i].rho+"   "+particles[i].pos.ToString("f12"));//+"  "+maxV.ToString("f6")+"  "+minV.ToString("f6")+"  ");
        //                 // Debug.Log(i+"   "+particles[i].rho.ToString("f6"));//+"  "+maxV.ToString("f6")+"  "+minV.ToString("f6")+"  ");
        //             maxRho = Mathf.Max(maxRho,particles[i].rho);
        //             minRho = Mathf.Min(minRho,particles[i].rho);
        //             // if(particles[i].vel.sqrMagnitude>maxV.sqrMagnitude) 
        //             //     maxV = particles[i].vel;
        //             // if(particles[i].vel.sqrMagnitude<maxV.sqrMagnitude) 
        //             //     minV = particles[i].vel;
        //         }
        //         if(showRho)
        //             Debug.Log(frameCnt+"  "+maxRho.ToString("f6")+"  "+minRho.ToString("f6"));//+"  "+maxV.ToString("f6")+"  "+minV.ToString("f6")+"  ");
        //     }
        //     if(showNeiNum)
        //     {
        //         // Debug.Log("Neighbor Number--------------------------------------------------------------");
        //         cb_neighborNum.GetData(arrayNeighborNum);
        //         cb_neighborIndices.GetData(arrayNeighborIndices);
        //         for(int i = 0;i<arrayNeighborNum.Length;i++)
        //         {
        //             if(arrayNeighborNum[i]<=0) continue;
        //             var s = "";
        //             for(int j = 0;j<arrayNeighborNum[i];j++)
        //                 s+="  "+arrayNeighborIndices[maxNumNeighbor*i+j].ToString();
        //             Debug.Log("particle idx and neighbor number  "+i+"  "+arrayNeighborNum[i]+"    neibor indices"+s);
        //         }
        //         Debug.Log("max neighbor number "+arrayNeighborNum.Max());
        //     }
        //     if(showBounds)
        //     {
        //         // Debug.Log("bounds--------------------------------------------------------------------");
        //         cb_bounds.GetData(arrayBounds);
        //         for(int i = 0;i<2;i++)
        //         {
        //             Debug.Log(i+"             " +arrayBounds[200*i+0]+"   "+arrayBounds[200*i+1]+"   "
        //                         +arrayBounds[200*i+2]+"   "+arrayBounds[200*i+3]+"   "
        //                         +arrayBounds[200*i+4]);
        //             for(int j =0;j<28;j++)
        //                 Debug.Log(i+"  "+j+"             "+arrayBounds[200*i+5+4*j]+" "+arrayBounds[200*i+5+4*j+1]
        //                             +" "+arrayBounds[200*i+5+4*j+2]+" "+arrayBounds[200*i+5+4*j+3]);
        //         }
        //     }
        // }

        mat_helper.SetFloat("_Thickness",thicknessFactor);
        mat_helper.SetFloat("_Size",particleRadius*2*scl);
        mat_helper.SetFloat("_Scale",scl);
        mat_helper.SetFloat("_FilterRadius",filterRadius);
        mat_helper.SetFloat("_ThickParticleRadiusSwellFactor",thickParticleRadiusSwellFactor);
        mat_helper.SetMatrix("_MatrixM",transform.localToWorldMatrix);
        mat_particleSurface.SetColor("_Color",surfaceColor);
        mat_particleSurface.SetFloat("_RefractIndex",refractIndex);
    }
    void OnRenderObject()
    {   
        if(renderParticle)
        {
            mat_helper.SetPass(5);
            Graphics.DrawProceduralNow(MeshTopology.Points,particleNum);
            return;
        }

        Graphics.SetRenderTarget(rt_surfaceColor);
        if(renderTemperature)
        {
            GL.Clear(true,true,Color.black);
            mat_helper.SetPass(6);
            Graphics.DrawProceduralNow(MeshTopology.Points,particleNum);
             //blur thickness texture
            for(int i = 0;i<5;i++)
            {
                mat_helper.SetFloat("horizontal",1.0f);
                Graphics.Blit(rt_surfaceColor,rt_surfaceColorBlurred,mat_helper,7);
                mat_helper.SetFloat("horizontal",0.0f);
                Graphics.Blit(rt_surfaceColorBlurred,rt_surfaceColor,mat_helper,7);
            }
        }
        else
            GL.Clear(true,true,surfaceColor);

        //get depth texture
        Graphics.SetRenderTarget(rt_depth);
        GL.Clear(true,true,Color.black);
        mat_helper.SetPass(0);
        Graphics.DrawProceduralNow(MeshTopology.Points,particleNum);

        //get thickness texture
        Graphics.SetRenderTarget(rt_thickness);
        GL.Clear(true,true,Color.black);
        mat_helper.SetPass(1);
        Graphics.DrawProceduralNow(MeshTopology.Points,particleNum);

        //blur thickness texture
        mat_helper.SetFloat("horizontal",1.0f);
        Graphics.Blit(rt_thickness,rt_thicknessBlurred,mat_helper,7);
        mat_helper.SetFloat("horizontal",0.0f);
        Graphics.Blit(rt_thicknessBlurred,rt_thickness,mat_helper,7);

        //blur depth texture
        Graphics.Blit(rt_depth,rt_depthBlurred,mat_helper,3);        
       
        //get normal texture using depth texture and blur
        Graphics.Blit(rt_depthBlurred,rt_normal,mat_helper,2);
        mat_helper.SetFloat("horizontal",1.0f);
        Graphics.Blit(rt_normal,rt_normalBlurred,mat_helper,7);
        mat_helper.SetFloat("horizontal",0.0f);
        Graphics.Blit(rt_normalBlurred,rt_normal,mat_helper,7);
    
        mat_particleSurface.SetTexture("_NormalTex",rt_normal);
        mat_particleSurface.SetTexture("_ThicknessTex",rt_thickness);
        mat_particleSurface.SetTexture("_ColoreTex",rt_surfaceColor);
        mat_particleSurface.SetTexture("_DepthTex",rt_depthBlurred);
        Graphics.Blit(null,null, mat_particleSurface);
    }
    void OnDestroy()
    {
        cb_indicesInCell.Dispose();
        cb_neighborIndices.Dispose();
        cb_neighborNum.Dispose();
        cb_numInCell.Dispose();
        cb_particle.Dispose();
    }
}