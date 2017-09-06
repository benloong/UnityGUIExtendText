using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class ExtendText : Text , IPointerClickHandler
{
    /// <summary>
    ///  表情格式 [[00]]
    ///  链接格式 [[连接名@url]]
    /// </summary>
    private static Regex emojRegex = new Regex(@"(?<emoj>(\[\[[0-9]{2}\]\]))|(?<href>(\[\[(.+?)\@(.+?)\]\]))");

    private const string underline = "_";

    UIVertex[] underlineVerts = new UIVertex[4];

    /// <summary>
    /// 表情数据库
    /// </summary>
    [SerializeField] EmojDatabase emojDatabase;

    /// <summary>
    /// 链接颜色
    /// </summary>
    [SerializeField] Color linkColor = Color.blue;

    /// <summary>
    /// 链接下划线颜色
    /// </summary>
    [SerializeField] Color linkUnderlineColor = Color.blue;

    [System.Serializable]
    public class HrefClickedEvent : UnityEvent<string>
    {
    }

    [SerializeField] HrefClickedEvent onHrefClick;

    public HrefClickedEvent OnHrefClick
    {
        get
        {
            return onHrefClick;
        }
        set
        {
            onHrefClick = value;
        }
    }

    /// <summary>
    /// 生成后的富文本 包含了自动生成的占位符<quad> <color>等标签
    /// </summary>
    string internalText;

    struct EmojTag
    {
        /// <summary>
        /// 开始字符位置 internalText Index
        /// </summary>
        public int charIndex;

        /// <summary>
        /// 哪个 Emoj
        /// </summary>
        public Emoj emoj;
    }

    class HrefTag
    {
        /// <summary>
        /// 开始字符位置 internalText Index
        /// </summary>
        public int beginCharIndex;

        /// <summary>
        /// 结束字符位置 internalText Index
        /// </summary>
        public int endCharIndex;

        /// <summary>
        /// 描述 显示用
        /// </summary>
        public string desc;

        /// <summary>
        /// 指向链接
        /// </summary>
        public string link;

        /// <summary>
        /// 碰撞盒
        /// </summary>
        public List<Rect> boxes = new List<Rect>();
    }

    /// <summary>
    /// Emoj组
    /// </summary>
    RectTransform emojRoot;

    /// <summary>
    /// emoj 标记表
    /// </summary>
    List<EmojTag> emojTags = new List<EmojTag>();

    /// <summary>
    /// 链接列表
    /// </summary>
    List<HrefTag> hrefTags = new List<HrefTag>();

    static readonly UIVertex[] m_TempVerts = new UIVertex[4];
    protected override void OnPopulateMesh(VertexHelper toFill)
    {
        if (font == null)
            return;

        // We don't care if we the font Texture changes while we are doing our Update.
        // The end result of cachedTextGenerator will be valid for this instance.
        // Otherwise we can get issues like Case 619238.
        m_DisableFontTextureRebuiltCallback = true;

        Vector2 extents = rectTransform.rect.size;

        var settings = GetGenerationSettings(extents);
        cachedTextGenerator.PopulateWithErrors(internalText, settings, gameObject);

        // Apply the offset to the vertices
        IList<UIVertex> verts = cachedTextGenerator.verts;

        // 隐藏方块
        ClearEmojQuads(verts);

        float unitsPerPixel = 1 / pixelsPerUnit;
        //Last 4 verts are always a new line... (\n)
        int vertCount = verts.Count - 4;

        Vector2 roundingOffset = new Vector2(verts[0].position.x, verts[0].position.y) * unitsPerPixel;
        roundingOffset = PixelAdjustPoint(roundingOffset) - roundingOffset;
        toFill.Clear();
        if (roundingOffset != Vector2.zero)
        {
            for (int i = 0; i < vertCount; ++i)
            {
                int tempVertsIndex = i & 3;
                m_TempVerts[tempVertsIndex] = verts[i];
                m_TempVerts[tempVertsIndex].position *= unitsPerPixel;
                m_TempVerts[tempVertsIndex].position.x += roundingOffset.x;
                m_TempVerts[tempVertsIndex].position.y += roundingOffset.y;
                if (tempVertsIndex == 3)
                    toFill.AddUIVertexQuad(m_TempVerts);
            }
        }
        else
        {
            for (int i = 0; i < vertCount; ++i)
            {
                int tempVertsIndex = i & 3;
                m_TempVerts[tempVertsIndex] = verts[i];
                m_TempVerts[tempVertsIndex].position *= unitsPerPixel;
                if (tempVertsIndex == 3)
                    toFill.AddUIVertexQuad(m_TempVerts);
            }
        }

        m_DisableFontTextureRebuiltCallback = false;

        // 更新表情位置
        PositionEmojs(verts);

        // 生成链接碰撞盒
        GenerateHrefHitBox(verts);

        // 生成链接下划线
        GenerateUnderline(toFill, settings);
    }

    void ClearEmojQuads(IList<UIVertex> verts)
    {
        foreach (var item in emojTags)
        {
            int vIndex = item.charIndex * 4;
            if ((vIndex + 4) > verts.Count) // 被隐藏了，表情也隐藏
                break;
            for (int i = vIndex; i < vIndex + 4; i++)
            {
                //清除Quad
                UIVertex tempVertex = verts[i];
                tempVertex.uv0 = Vector2.zero;
                tempVertex.color = new Color32(0, 0, 0, 0);
                verts[i] = tempVertex;
            }
        }
    }

    void PositionEmojs(IList<UIVertex> verts)
    {
        for (int i = 0; i < emojTags.Count; i++)
        {
            var child = emojRoot.GetChild(i);

            var childGraphic = child.GetComponent<Graphic>();

            int vIndex = emojTags[i].charIndex * 4;
            if ((vIndex + 4) >= verts.Count)
            {
                // 直接改alpha会引起Trying to XXX for graphic rebuild while we are already inside a graphic rebuild loop. This is not supported.
                // 这里改alpha为0 隐藏
                childGraphic.CrossFadeAlpha(0, 0f, true);
                continue;
            }

            Vector2 min = verts[vIndex].position;
            Vector2 max = verts[vIndex].position;

            for (int j = vIndex; j < vIndex + 4; j++)
            {
                Vector2 pos = verts[j].position;
                min.x = Mathf.Min(min.x, pos.x);
                min.y = Mathf.Min(min.y, pos.y);

                max.x = Mathf.Max(max.x, pos.x);
                max.y = Mathf.Max(max.y, pos.y);
            }

            childGraphic.CrossFadeAlpha(1, 0f, true);
            child.localPosition = min + (max - min) / 2;
        }
    }

    static System.Text.StringBuilder stringBuilder = new System.Text.StringBuilder();

    /// <summary>
    /// 分析处理文本，得到tag和渲染文本
    /// </summary>
    /// <param name="text">原始文本</param>
    /// <returns>渲染文本</returns>
    void  ProcessText(string text)
    {
        if (string.IsNullOrEmpty(text) == true)
        {
            internalText = "";
            return;
        }

        if (emojDatabase == null)
        {
            internalText = text;
            return;
        }

        emojTags.Clear();
        hrefTags.Clear();

        // clear string buffer
        stringBuilder.Length = 0;

        {
            int index = 0;

            for (Match match = emojRegex.Match(text); match.Success; match = match.NextMatch())
            {
                string val = match.Value;

                stringBuilder.Append(text.Substring(index, match.Index - index));
                index = match.Index + val.Length;

                string href = match.Groups["href"].Value;
                string emoj = match.Groups["emoj"].Value;

                if (string.IsNullOrEmpty(emoj) == false)
                {

                    var emojId = val.Substring(2, 2);

                    var emojInfo = emojDatabase.GetEmoj(emojId);

                    if (emojInfo != null)
                    {
                        int charIndex = stringBuilder.Length;
                        emojTags.Add(new EmojTag() { charIndex = charIndex, emoj = emojInfo });
                        Vector2 emojSize = emojInfo.Size;
                        stringBuilder.AppendFormat("<quad size={0}>", Mathf.Max(emojSize.x, emojSize.y).ToString());
                    }
                    else
                    {
                        stringBuilder.Append(val);
                    }
                }
                else if (string.IsNullOrEmpty(href) == false)
                {
                    var content = val.Replace("[[", "").Replace("]]", "");
                    var pivot = content.IndexOf('@');

                    var desc = content.Substring(0, pivot);
                    var link = content.Substring(pivot + 1);

                    int charIndex = stringBuilder.Length;

                    Color32 linkColor = this.linkColor;
                    string color = string.Format("{0}{1}{2}{3}", linkColor.r.ToString("x2"), linkColor.g.ToString("x2"), linkColor.b.ToString("x2"), linkColor.a.ToString("x2"));
                    stringBuilder.AppendFormat("<color=#{0}>{1}</color>",color, desc);
                    hrefTags.Add(new HrefTag() { beginCharIndex = charIndex, endCharIndex = stringBuilder.Length, desc = desc, link = link });
                }
                else
                {
                    stringBuilder.Append(val);
                }
            }

            stringBuilder.Append(text.Substring(index));
        }

        internalText = stringBuilder.ToString();

        GenerateEmojs();
    }

    void GenerateEmojs()
    {
        if (emojRoot == null)
        {
            emojRoot = rectTransform;
        }

        for (int i = 0; i < emojTags.Count; i++)
        {
            RectTransform rect = null;
            if (i < emojRoot.childCount)
            {
                rect = emojRoot.GetChild(i).GetComponent<RectTransform>();
                rect.gameObject.SetActive(true);
            }
            else
            {
                rect = new GameObject().AddComponent<RectTransform>();
                rect.gameObject.hideFlags = HideFlags.HideAndDontSave; 
                rect.SetParent(emojRoot, false);
                rect.gameObject.AddComponent<EmojImage>();
                rect.GetComponent<Graphic>().raycastTarget = false;
            }

            rect.GetComponent<EmojImage>().Emoj = emojTags[i].emoj;
        }

        for (int i = emojTags.Count; i < emojRoot.childCount; i++)
        {
            emojRoot.GetChild(i).gameObject.SetActive(false);
        }
    }

    void GenerateHrefHitBox(IList<UIVertex> verts)
    {
        foreach (var tag in hrefTags)
        {
            int begin = tag.beginCharIndex * 4;
            int end = tag.endCharIndex * 4;

            if (verts.Count < begin)
            {
                // 后面的顶点被隐藏了
                break;
            }

            var boundsList = tag.boxes;
            boundsList.Clear();

            Bounds bounds = new Bounds(verts[begin].position, Vector3.zero);

            for (int i = begin; i < end && i < verts.Count - 4; i++) // 最后4个顶点是换行符
            {
                var pos = verts[i].position;
               
                if (pos.x < bounds.min.x) // 换行
                {
                    if (bounds.size.x > 0 && bounds.size.y > 0) // 检测不是个空盒子
                    {
                        tag.boxes.Add(new Rect(bounds.min, bounds.size));
                    }
                    bounds = new Bounds(pos, Vector3.zero);
                }
                else
                {
                    bounds.Encapsulate(pos);
                }
            }

            if (bounds.size.x > 0 && bounds.size.y > 0) // 检测不是个空盒子
            {
                tag.boxes.Add(new Rect(bounds.min, bounds.size));
            }
        }
    }

    void GenerateUnderline(VertexHelper toFill, TextGenerationSettings settings)
    {
        UIVertex[] underlineVerts = GetUnderlineVerts(settings);

        foreach (var tag in hrefTags)
        {
            foreach (var box in tag.boxes)
            {
                if (box.width > 0 && box.height > 0)
                {
                    FillUnderline(box, toFill, underlineVerts);
                }
            }
        }        
    }

    void FillUnderline(Rect box, VertexHelper toFill, UIVertex[] underlineVerts)
    {
        /* 面片结构
         * p0-------p1
         *  |       | 
         *  |       |
         * p3-------p2
         * */

        box.position -= new Vector2(0, 1); //向下偏移一个单位

        Vector3 p0 = underlineVerts[0].position;
        Vector3 p1 = underlineVerts[1].position;
        Vector3 p2 = underlineVerts[2].position;

        float height = p1.y - p2.y;
        float width = p1.x - p0.x;

        Vector2 uv0 = underlineVerts[0].uv0;
        Vector2 uv1 = underlineVerts[1].uv0;
        Vector2 uv2 = underlineVerts[2].uv0;
        Vector2 uv3 = underlineVerts[3].uv0;

        //顶部中心uv
        Vector2 topCenterUv = uv0 + (uv1 - uv0) * 0.5f;
        //底部中心uv
        Vector2 bottomCenterUv = uv3 + (uv2 - uv3) * 0.5f;

        m_TempVerts[0] = underlineVerts[0];
        m_TempVerts[1] = underlineVerts[1];
        m_TempVerts[2] = underlineVerts[2];
        m_TempVerts[3] = underlineVerts[3];

        m_TempVerts[0].color = linkUnderlineColor;
        m_TempVerts[1].color = linkUnderlineColor;
        m_TempVerts[2].color = linkUnderlineColor;
        m_TempVerts[3].color = linkUnderlineColor;

        float xMin = box.xMin;
        float yMin = box.yMin;

        float xMax = box.xMax;
        float yMax = box.yMin + height; // 高度

        {
            m_TempVerts[0].position = new Vector3(xMin, yMax);
            m_TempVerts[0].uv0 = uv0;

            m_TempVerts[1].position = new Vector3(xMin + width * 0.5f, yMax);
            m_TempVerts[1].uv0 = topCenterUv;

            m_TempVerts[2].position = new Vector3(xMin + width * 0.5f, yMin);
            m_TempVerts[2].uv0 = bottomCenterUv;

            m_TempVerts[3].position = new Vector3(xMin, yMin);
            m_TempVerts[3].uv0 = uv3;

            toFill.AddUIVertexQuad(m_TempVerts);
        }
        {
            m_TempVerts[0].position = new Vector3(xMin + width * 0.5f, yMax);
            m_TempVerts[0].uv0 = topCenterUv;

            m_TempVerts[1].position = new Vector3(xMax - width * 0.5f, yMax);
            m_TempVerts[1].uv0 = topCenterUv;

            m_TempVerts[2].position = new Vector3(xMax - width * 0.5f, yMin);
            m_TempVerts[2].uv0 = bottomCenterUv;

            m_TempVerts[3].position = new Vector3(xMin + width * 0.5f, yMin);
            m_TempVerts[3].uv0 = bottomCenterUv;

            toFill.AddUIVertexQuad(m_TempVerts);
        }

        {
            m_TempVerts[0].position = new Vector3(xMax - width * 0.5f, yMax);
            m_TempVerts[0].uv0 = topCenterUv;

            m_TempVerts[1].position = new Vector3(xMax, yMax);
            m_TempVerts[1].uv0 = uv1;

            m_TempVerts[2].position = new Vector3(xMax, yMin);
            m_TempVerts[2].uv0 = uv2;

            m_TempVerts[3].position = new Vector3(xMax - width * 0.5f, yMin);
            m_TempVerts[3].uv0 = bottomCenterUv;

            toFill.AddUIVertexQuad(m_TempVerts);
        }
    }

    TextGenerator underlineTextGenerator;
    UIVertex[] GetUnderlineVerts(TextGenerationSettings settings)
    {
        if (underlineTextGenerator == null)
        {
            underlineTextGenerator = new TextGenerator();
        }

        float unitsPerPixel = 1 / pixelsPerUnit;
        cachedTextGenerator.Populate(underline, settings);
        var verts = cachedTextGenerator.verts;

        for (int i = 0; i < verts.Count && i < 4; i++)
        {
            underlineVerts[i] = verts[i];
            underlineVerts[i].position *= unitsPerPixel;
        }

        return underlineVerts;
    }

    public override float preferredWidth
    {
        get
        {
            var settings = GetGenerationSettings(Vector2.zero);
            return cachedTextGeneratorForLayout.GetPreferredWidth(internalText, settings) / pixelsPerUnit;
        }
    }

    public override float preferredHeight
    {
        get
        {
            var settings = GetGenerationSettings(new Vector2(rectTransform.rect.size.x, 0.0f));
            return cachedTextGeneratorForLayout.GetPreferredHeight(internalText, settings) / pixelsPerUnit;
        }
    }

    public override void SetVerticesDirty()
    {
        ProcessText(text);
        base.SetVerticesDirty();
    }

    protected override void OnDisable()
    {
        SetEnableChild(false);
        base.OnDisable();
    }

    protected override void OnEnable()
    {
        ForceRichTextAndAlignByGeometry();
        base.OnEnable();
        SetEnableChild(true);
    }

    void SetEnableChild(bool enable)
    {
        for (int i = 0; emojRoot != null && i < emojRoot.childCount; i++)
        {
            emojRoot.GetChild(i).GetComponent<Graphic>().enabled = enable;
        }
    }

    protected void ForceRichTextAndAlignByGeometry()
    {
        supportRichText = true;
        alignByGeometry = true;
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        Vector2 lp;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            rectTransform, eventData.position, eventData.pressEventCamera, out lp);

        foreach (var hrefInfo in hrefTags)
        {
            var boxes = hrefInfo.boxes;
            for (var i = 0; i < boxes.Count; ++i)
            {
                if (boxes[i].Contains(lp))
                {
                    OnHrefClick.Invoke(hrefInfo.link);
                    return;
                }
            }
        }
    }

#if UNITY_EDITOR
    protected override void OnValidate()
    {
        ForceRichTextAndAlignByGeometry();
        base.OnValidate();
    }
#endif

}
