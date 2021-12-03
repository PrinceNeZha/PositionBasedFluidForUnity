using UnityEngine;
using System.Collections;
 
public class CameraController : MonoBehaviour {
 
    /*
    Writen by Windexglow 11-13-10.  Use it, edit it, steal it I don't care.  
    Converted to C# 27-02-13 - no credit wanted.
    Simple flycam I made, since I couldn't find any others made public.  
    Made simple to use (drag and drop, done) for regular keyboard layout  
    wasd : basic movement
    shift : Makes camera accelerate
    space : Moves camera on X and Z axis only.  So camera doesn't gain any height*/
     
     
    public float mainSpeed = 100.0f; //regular speed
    float shiftAdd = 250.0f; //multiplied by how long shift is held.  Basically running
    float maxShift = 1000.0f; //Maximum speed when holdin gshift
    float camSens = 0.25f; //How sensitive it with mouse
    private Vector3 lastMouse = new Vector3(255, 255, 255); //kind of in the middle of the screen, rather than at the top (play)
    private float totalRun= 1.0f;
     
    Material mat_particleSurface;
    void Start()
    {
        mat_particleSurface = new Material(Shader.Find("MJD/particleSurfaceShading"));
    }

    void Update () {

        lastMouse = Input.mousePosition - lastMouse ;
        lastMouse = new Vector3(-lastMouse.y * camSens, lastMouse.x * camSens, 0 );
        lastMouse = new Vector3(transform.eulerAngles.x + lastMouse.x , transform.eulerAngles.y + lastMouse.y, 0);
        if(Input.GetKey(KeyCode.Mouse1)){
        transform.eulerAngles = lastMouse;
        }
        lastMouse =  Input.mousePosition;
        //Mouse  camera angle done.  
       
        //Keyboard commands
        // float f = 0.0f;
        Vector3 p = GetBaseInput();
        if (p.sqrMagnitude > 0){ // only move while a direction key is pressed
          if (Input.GetKey (KeyCode.LeftShift)){
              totalRun += Time.deltaTime;
              p  = p * totalRun * shiftAdd;
              p.x = Mathf.Clamp(p.x, -maxShift, maxShift);
              p.y = Mathf.Clamp(p.y, -maxShift, maxShift);
              p.z = Mathf.Clamp(p.z, -maxShift, maxShift);
          } else {
              totalRun = Mathf.Clamp(totalRun * 0.5f, 1f, 1000f);
              p = p * mainSpeed;
          }
         
          p = p * Time.deltaTime;
          Vector3 newPosition = transform.position;
          if (Input.GetKey(KeyCode.Space)){ //If player wants to move on X and Z axis only
              transform.Translate(p);
              newPosition.x = transform.position.x;
              newPosition.z = transform.position.z;
              transform.position = newPosition;
          } else {
              transform.Translate(p);
          }
        }
    }
     
    private Vector3 GetBaseInput() { //returns the basic values, if it's 0 than it's not active.
        Vector3 p_Velocity = new Vector3();
        if (Input.GetKey (KeyCode.W) && Input.GetKey(KeyCode.Mouse1)){
            p_Velocity += new Vector3(0, 0 , 1);
        }
        if (Input.GetKey (KeyCode.S)&& Input.GetKey(KeyCode.Mouse1)){
            p_Velocity += new Vector3(0, 0, -1);
        }
        if (Input.GetKey (KeyCode.A)&& Input.GetKey(KeyCode.Mouse1)){
            p_Velocity += new Vector3(-1, 0, 0);
        }
        if (Input.GetKey (KeyCode.D)&& Input.GetKey(KeyCode.Mouse1)){
            p_Velocity += new Vector3(1, 0, 0);
        }
        return p_Velocity;
    }
    public FluidManager fluidScript;

    void OnRenderImage(RenderTexture src,RenderTexture dst)
    {
        RenderTexture rt_surfaceColor = fluidScript.GetColor();
        mat_particleSurface.SetTexture("_ColoreTex",rt_surfaceColor);
        if(fluidScript.renderParticle) 
        {
            Graphics.Blit(src,dst,mat_particleSurface,1);
            return;
        }
        RenderTexture rt_normal = fluidScript.GetNormal();
        RenderTexture rt_thickness = fluidScript.GetThickness();
        RenderTexture rt_depthBlurred = fluidScript.GetDepth();
        
        mat_particleSurface.SetTexture("_NormalTex",rt_normal);
        mat_particleSurface.SetTexture("_ThicknessTex",rt_thickness);
        mat_particleSurface.SetTexture("_DepthTex",rt_depthBlurred);

        Graphics.Blit(src,dst,mat_particleSurface,0);
    }
}