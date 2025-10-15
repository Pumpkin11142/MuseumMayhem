using System.Collections;
using UnityEngine;
using Mirror;

public class ShardFade : NetworkBehaviour
{
    public float fadeDuration = 2f;
    private Material mat;
    private Color startColor;

    void Start()
    {
        var rend = GetComponentInChildren<MeshRenderer>();
        if (rend)
        {
            mat = rend.material;
            startColor = mat.color;
            StartCoroutine(FadeOut());
        }
    }

    IEnumerator FadeOut()
    {
        float t = 0;
        while (t < fadeDuration)
        {
            t += Time.deltaTime;
            if (mat)
            {
                Color c = startColor;
                c.a = Mathf.Lerp(1, 0, t / fadeDuration);
                mat.color = c;
            }
            yield return null;
        }
        if (isServer) NetworkServer.Destroy(gameObject);
        else Destroy(gameObject);
    }
}
