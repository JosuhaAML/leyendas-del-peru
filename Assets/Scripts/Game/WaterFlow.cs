using UnityEngine;

// Agua que se mueve: desplaza la textura (y su relieve) poco a poco para que las ondas fluyan.
public class WaterFlow : MonoBehaviour
{
    public Vector2 speed = new Vector2(0.02f, 0.035f);
    Material mat;

    void Start() => mat = GetComponent<Renderer>().material;

    void Update()
    {
        if (mat == null) return;
        mat.mainTextureOffset = speed * Time.time;
    }
}
