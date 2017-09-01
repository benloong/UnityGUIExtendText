using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public class Emoj
{
    public string id;
    /// <summary>
    /// 表情帧，用于实现帧动画
    /// </summary>
    public Sprite[] spriteList;

    public Vector2 Size
    {
        get
        {
            if (spriteList.Length == 0)
            {
                return Vector2.one;
            }

            Vector2 sz = spriteList[0].rect.size;
            for (int i = 1; i < spriteList.Length; i++)
            {
                sz = Vector2.Max(sz, spriteList[i].rect.size);
            }
            return sz;
        }
    }

    public float size
    {
        get
        {
            if (spriteList.Length == 0)
            {
                return 0;
            }
            float w = 14;
            float h = 14;
            for (int i = 0; i < spriteList.Length; i++)
            {
                w = Mathf.Max(spriteList[i].rect.width, w);
                h = Mathf.Max(spriteList[i].rect.height, h);
            }
            return Mathf.Max(w, h);
        }
    }
}

[CreateAssetMenu]
public class EmojDatabase : ScriptableObject
{
    public Emoj[] emojs;

    public Emoj GetEmoj(string id)
    {
        for (int i = 0; i < emojs.Length; i++)
        {
            if (emojs[i].id == id)
            {
                return emojs[i];
            }
        }

        return null;
    }
}
