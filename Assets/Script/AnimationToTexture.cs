using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Funcy.Graphics;
using UnityEngine.Rendering;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Animations;
#endif
public class AnimationToTexture : MonoBehaviour
{
    [SerializeField] Animator anim;
    [SerializeField] ComputeShader recordVertexData;
    [SerializeField] SkinnedMeshRenderer ren;

    //[SerializeField] RenderTexture tmp;



    Buffer positionOS_Buffer, normalOS_Buffer, tangentOS_Buffer;

    public float sampleCount = 256;
    private void OnEnable()
    {
        InitializeAllBuffer();
        //AsyncGPUReadback.Request(triangleCountBuffer, r1 => OnTriangleCountAvailable(r1, Time.frameCount));
    }

    void InitializeAllBuffer()
    {
        positionOS_Buffer = new Buffer(Mathf.CeilToInt(ren.sharedMesh.vertexCount * sampleCount * 3), typeof(Vector3));
        normalOS_Buffer = new Buffer(Mathf.CeilToInt(ren.sharedMesh.vertexCount * sampleCount * 3), typeof(Vector3));
        tangentOS_Buffer = new Buffer(Mathf.CeilToInt(ren.sharedMesh.vertexCount * sampleCount * 3), typeof(Vector4));
    }

    RenderTexture CreateRenderTexture(string name, Vector2 size, RenderTextureFormat format, Color defalutFillColor)
    {
        var map = new RenderTexture(Mathf.CeilToInt(size.x), Mathf.CeilToInt(size.y), 0, format);
        map.enableRandomWrite = true;
        map.wrapMode = TextureWrapMode.Repeat;
        map.Create();

        RenderTexture.active = map;
        GL.Clear(true, true, defalutFillColor);
        RenderTexture.active = null;

        return map;
    }


    IEnumerator WaitForFrame(int frameCount)
    {
        for (int i = 0; i < frameCount; i++) yield return null;
    }
#if UNITY_EDITOR
    public bool record = false;
    private void Start()
    {
        if (record) StartCoroutine(StartRecord());
    }
    public IEnumerator StartRecord()
    {
        while (EditorApplication.isPaused) yield return null;

        
        var controller = (AnimatorController)anim.runtimeAnimatorController;

        int width = Mathf.CeilToInt(sampleCount * 3);
        int height = ren.sharedMesh.vertexCount;

        var vertexDataMap = CreateRenderTexture("vertexDataMap", new Vector2(width, height), RenderTextureFormat.ARGBHalf, Color.clear);
        
        var stateName = controller.layers[0].stateMachine.defaultState.name;
        anim.Play(stateName, 0);
        yield return StartCoroutine(WaitForFrame(5));

        List<Vector3> positionList = new List<Vector3>();
        List<Vector3> normalList = new List<Vector3>();
        List<Vector4> tangentList = new List<Vector4>();
        float lerpDetla = 1.0f / sampleCount;
        float currentTime = 0.0f;
        Mesh currentMesh = new Mesh();
        while (true)
        {
            currentMesh.Clear();
            anim.Play(stateName, 0, currentTime);
            currentTime += lerpDetla;
            yield return null;
            ren.BakeMesh(currentMesh);
            positionList.AddRange(currentMesh.vertices);
            normalList.AddRange(currentMesh.normals);
            tangentList.AddRange(currentMesh.tangents);
            if (currentTime >= 1.0f) break;
            //Debug.Log("Rec");
        }

        positionOS_Buffer.SetData(positionList.ToArray());
        normalOS_Buffer.SetData(normalList.ToArray());
        tangentOS_Buffer.SetData(tangentList.ToArray());

        DestroyImmediate(currentMesh);
        var kernel = recordVertexData.FindKernel("CSMain");
        uint x, y, z;
        recordVertexData.SetInt("vertexCount", ren.sharedMesh.vertexCount);
        recordVertexData.SetInt("sampleCount", (int)sampleCount);        
        recordVertexData.SetBuffer(kernel, "positionOS", positionOS_Buffer.target);
        recordVertexData.SetBuffer(kernel, "normalOS", normalOS_Buffer.target);
        recordVertexData.SetBuffer(kernel, "tangentOS", tangentOS_Buffer.target);
        
        recordVertexData.SetTexture(kernel, "Result", vertexDataMap);
        recordVertexData.GetKernelThreadGroupSizes(kernel, out x, out y, out z);
        recordVertexData.Dispatch(kernel, Mathf.CeilToInt(vertexDataMap.width / (float)x), Mathf.CeilToInt(vertexDataMap.height / (float)y), Mathf.CeilToInt(z));

        var vertexDataTex = Convert(vertexDataMap);
        Graphics.CopyTexture(vertexDataMap, vertexDataTex);
        AssetDatabase.CreateAsset(vertexDataTex, "Assets/" + controller.layers[0].stateMachine.defaultState.motion.name + ".asset");

        yield return null;            
    }

    Texture2D Convert(RenderTexture rt)
    {
        var map = new Texture2D(rt.width, rt.height, TextureFormat.RGBAHalf, false);
        RenderTexture.active = rt;
        map.ReadPixels(Rect.MinMaxRect(0, 0, rt.width, rt.height), 0, 0);
        RenderTexture.active = null;
        return map;
    }

#endif
    private void OnDisable()
    {
        Buffer.DisposeAll();
    }
}
#if UNITY_EDITOR
[CustomEditor(typeof(AnimationToTexture))]
public class AnimationToTexture_Editor : Editor
{
    AnimationToTexture data;
    private void OnEnable()
    {
        data = target as AnimationToTexture;
    }
    public override void OnInspectorGUI()
    {
        if(GUILayout.Button("Start Record"))
        {            
            EditorApplication.isPlaying = true;           
        }
        base.OnInspectorGUI();
    }
}
#endif