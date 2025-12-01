using UnityEngine;

public class TextureScroller : MonoBehaviour
{
    [SerializeField] private Vector2 _scrollSpeed = new Vector2(0.5f, 0.5f);


    [SerializeField] private Renderer _renderer;

    private Material _material;

    private void Start()
    {
        _material = _renderer.material;
    }

    private void Update()
    {
        _material.mainTextureOffset = _scrollSpeed * Time.time;
    }
}
