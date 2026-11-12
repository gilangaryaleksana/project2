using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using TMPro;

public class ARModelSpawner : MonoBehaviour
{
    public Camera arCamera;                    // Main Camera di XR Origin
    public ARRaycastManager raycastManager;    // XR Origin
    public ARAnchorManager anchorManager;      // XR Origin (kosongkan = dicari otomatis)
    public GameObject[] modelPrefabs;
    public string[] modelNames;
    public TMP_Text labelText;
    public GameObject deleteButton;            // tombol X
    public GameObject photoButton;             // tombol Foto (ikon kamera), hanya aktif kalau ada model

    [Header("Pratinjau Biru")]
    public Material ghostMaterial;             // material GhostBlue (transparan)

    [Header("Panel Sejarah (dibuat otomatis oleh script)")]
    public float panelWidthPercent = 0.42f;    // lebar panel (persen lebar layar)
    public float panelHeightPercent = 0.26f;   // tinggi panel (persen tinggi layar)
    public float panelGap = 30f;               // jarak panel dari tepi model (piksel)
    public float bottomReservePercent = 0.22f; // area bawah layar yang disisakan untuk tombol

    [Header("Foto")]
    public GameObject buttonsCanvas;           // Canvas berisi tombol (disembunyikan saat foto)
    public bool includeInfoPanel = true;       // sertakan panel sejarah di foto
    public string albumName = "AR Mesin";      // nama album di galeri

    public float fallbackDistance = 1.5f;      // dipakai kalau belum ada bidang terdeteksi
    public float heightOffset = 0f;            // naikkan kalau model setengah tenggelam di meja

    private GameObject infoRoot;
    private RectTransform infoBox;
    private TMP_Text infoTitleText;
    private TMP_Text infoBodyText;

    private GameObject toastBox;
    private TMP_Text toastText;
    private GameObject hintBox;
    private TMP_Text hintText;
    private bool isCapturing;

    private GameObject ghost;                  // pratinjau biru
    private Material fallbackGhostMaterial;
    private bool hasGhostPose;
    private Vector3 ghostPos;
    private Quaternion ghostRot = Quaternion.identity;

    private int selectedIndex = 0;
    private GameObject current;                // model yang sudah terkunci
    private ARAnchor currentAnchor;             // anchor tempat model menempel
    private static List<ARRaycastHit> hits = new List<ARRaycastHit>();

    // Urutan harus sama dengan Model Prefabs di Inspector:
    // 0 electric motor, 1 motorcycle, 2 RotaryEngine, 3 Rotating Jet Engine, 4 stuart
    private static readonly string[] histories =
    {
        "Prinsip motor listrik bermula dari percobaan Michael Faraday pada 1821, yang menunjukkan bahwa arus listrik dapat menggerakkan kawat di dalam medan magnet. " +
        "Pada 1834 Moritz von Jacobi membuat motor listrik putar yang praktis, dan pada 1838 motor serupa dipakai menggerakkan sebuah perahu. " +
        "Pada 1888 Nikola Tesla mematenkan motor induksi arus bolak-balik, desain yang menjadi dasar sebagian besar motor listrik industri hingga sekarang.",

        "Prinsip mesin empat langkah (isap, kompresi, usaha, buang) diuraikan oleh Alphonse Beau de Rochas pada 1862. " +
        "Nikolaus Otto membangun mesin empat langkah praktis pada 1876, sehingga siklusnya dikenal sebagai siklus Otto. " +
        "Pada 1885 Gottlieb Daimler dan Wilhelm Maybach memasang mesin bensin berkecepatan tinggi pada kendaraan roda dua kayu bernama Reitwagen, yang dianggap sepeda motor pertama.",

        "Mesin rotari Wankel dikembangkan oleh insinyur Jerman Felix Wankel bersama perusahaan NSU, dan prototipe yang berhasil berjalan diuji pada 1957. " +
        "Mobil produksi pertama yang memakainya adalah NSU Spider pada 1964, lalu Mazda mempopulerkannya lewat Cosmo Sport pada 1967. " +
        "Alih-alih torak, mesin ini memakai rotor segitiga yang berputar di dalam rumah mesin.",

        "Konsep mesin jet dipatenkan oleh Frank Whittle di Inggris pada 1930, dan mesin buatannya pertama kali dijalankan pada 1937. " +
        "Di Jerman, Hans von Ohain mengembangkan mesin serupa yang menerbangkan pesawat Heinkel He 178 pada 27 Agustus 1939, penerbangan pertama dengan mesin jet. " +
        "Mesin ini menekan udara, membakarnya bersama bahan bakar, lalu membuang gas panas lewat turbin dan nozzle untuk menghasilkan dorongan.",

        "Mesin uap berawal dari mesin Thomas Newcomen pada 1712 yang dipakai memompa air dari tambang, lalu disempurnakan James Watt dengan kondensor terpisah (paten 1769). " +
        "Model ini mengacu pada mesin uap kecil buatan Stuart Turner, perusahaan Inggris yang berdiri pada 1906 dan terkenal dengan mesin uap model. " +
        "Prinsipnya: uap bertekanan mendorong torak sehingga menghasilkan gerak putar."
    };

    void Start()
    {
        if (anchorManager == null)
            anchorManager = FindAnyObjectByType<ARAnchorManager>();

        BuildInfoPanel();

        toastBox = CreateBanner("Toast", 100, -120f, 100f, out toastText);
        toastBox.SetActive(false);

        hintBox = CreateBanner("Hint", 90, -250f, 120f, out hintText);
        hintText.text = "Arahkan kamera ke lantai atau meja, lalu gerakkan HP pelan-pelan sampai pratinjau biru muncul";
        hintBox.SetActive(false);

        UpdateLabel();
        RebuildGhost();
        if (deleteButton != null) deleteButton.SetActive(false);
        if (photoButton != null) photoButton.SetActive(false);
        infoRoot.SetActive(false);
    }

    // ---------- Pratinjau biru ----------

    void RebuildGhost()
    {
        if (ghost != null) Destroy(ghost);
        if (modelPrefabs.Length == 0) return;

        ghost = Instantiate(modelPrefabs[selectedIndex]);
        ghost.name = "Pratinjau_" + modelPrefabs[selectedIndex].name;
        FreezeModel(ghost);

        Material mat = GetGhostMaterial();

        foreach (Collider c in ghost.GetComponentsInChildren<Collider>())
            c.enabled = false;

        foreach (Renderer r in ghost.GetComponentsInChildren<Renderer>())
        {
            if (r is MeshRenderer || r is SkinnedMeshRenderer)
            {
                if (mat != null)
                {
                    Material[] mats = new Material[r.sharedMaterials.Length];
                    for (int i = 0; i < mats.Length; i++) mats[i] = mat;
                    r.sharedMaterials = mats;
                }
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
            }
            else
            {
                r.enabled = false;
            }
        }

        ghost.SetActive(false);
    }

    Material GetGhostMaterial()
    {
        if (ghostMaterial != null) return ghostMaterial;

        if (fallbackGhostMaterial == null)
        {
            Shader sh = Shader.Find("Sprites/Default");
            if (sh == null) return null;
            fallbackGhostMaterial = new Material(sh);
            fallbackGhostMaterial.color = new Color(0.1f, 0.5f, 1f, 0.45f);
        }
        return fallbackGhostMaterial;
    }

    // Matikan fisika dan gerak akar animasi supaya model tidak berpindah sendiri
    void FreezeModel(GameObject model)
    {
        foreach (Rigidbody rb in model.GetComponentsInChildren<Rigidbody>())
        {
            rb.isKinematic = true;
            rb.useGravity = false;
        }
        foreach (Animator an in model.GetComponentsInChildren<Animator>())
            an.applyRootMotion = false;
    }

    // Hitung titik di bidang dari tengah layar, lalu gerakkan pratinjau ke sana
    void UpdateGhost()
    {
        Transform cam = arCamera.transform;
        Vector2 center = new Vector2(Screen.width / 2f, Screen.height / 2f);

        hasGhostPose = false;

        if (raycastManager != null &&
            raycastManager.Raycast(center, hits, TrackableType.PlaneWithinPolygon))
        {
            ghostPos = hits[0].pose.position + Vector3.up * heightOffset;
            hasGhostPose = true;
        }
#if UNITY_EDITOR
        else
        {
            // di editor tidak ada ARCore: anggap ada bidang di depan kamera supaya bisa dites
            ghostPos = cam.position + cam.forward * fallbackDistance;
            hasGhostPose = true;
        }
#endif

        if (hasGhostPose)
        {
            Vector3 dir = cam.position - ghostPos;
            dir.y = 0;
            ghostRot = dir.sqrMagnitude > 0.0001f
                ? Quaternion.LookRotation(dir)
                : Quaternion.identity;
        }

        bool showGhost = hasGhostPose && current == null && !isCapturing;
        if (ghost != null)
        {
            if (ghost.activeSelf != showGhost) ghost.SetActive(showGhost);
            if (showGhost) ghost.transform.SetPositionAndRotation(ghostPos, ghostRot);
        }

        if (hintBox != null)
            hintBox.SetActive(current == null && !hasGhostPose && !isCapturing);
    }

    // ---------- Panel sejarah (dibuat lewat kode) ----------

    void BuildInfoPanel()
    {
        infoRoot = new GameObject("InfoPanelCanvas");
        Canvas canvas = infoRoot.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 50;

        GameObject boxGO = new GameObject("InfoBox", typeof(RectTransform), typeof(Image));
        boxGO.transform.SetParent(infoRoot.transform, false);
        infoBox = boxGO.GetComponent<RectTransform>();
        infoBox.anchorMin = Vector2.zero;
        infoBox.anchorMax = Vector2.zero;
        infoBox.pivot = new Vector2(0.5f, 0.5f);

        Image bg = boxGO.GetComponent<Image>();
        bg.color = new Color(0.05f, 0.08f, 0.15f, 0.88f);
        bg.raycastTarget = false;

        infoTitleText = CreateText("Judul", boxGO.transform, 44f, FontStyles.Bold);
        RectTransform tr = infoTitleText.rectTransform;
        tr.anchorMin = new Vector2(0f, 1f);
        tr.anchorMax = new Vector2(1f, 1f);
        tr.pivot = new Vector2(0.5f, 1f);
        tr.anchoredPosition = new Vector2(0f, -20f);
        tr.sizeDelta = new Vector2(-40f, 70f);
        infoTitleText.alignment = TextAlignmentOptions.Left;

        infoBodyText = CreateText("Isi", boxGO.transform, 34f, FontStyles.Normal);
        RectTransform br = infoBodyText.rectTransform;
        br.anchorMin = Vector2.zero;
        br.anchorMax = Vector2.one;
        br.offsetMin = new Vector2(20f, 20f);
        br.offsetMax = new Vector2(-20f, -100f);
        infoBodyText.alignment = TextAlignmentOptions.TopLeft;
        infoBodyText.enableAutoSizing = true;
        infoBodyText.fontSizeMin = 24f;
        infoBodyText.fontSizeMax = 40f;
    }

    TMP_Text CreateText(string objName, Transform parent, float size, FontStyles style)
    {
        GameObject go = new GameObject(objName, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        TextMeshProUGUI t = go.AddComponent<TextMeshProUGUI>();
        t.fontSize = size;
        t.fontStyle = style;
        t.color = Color.white;
        t.raycastTarget = false;
        return t;
    }

    // Kotak pesan di atas layar (dipakai untuk "Foto tersimpan", "Model terkunci", dan petunjuk)
    GameObject CreateBanner(string objName, int sortOrder, float y, float height, out TMP_Text text)
    {
        GameObject root = new GameObject(objName + "Canvas");
        Canvas canvas = root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = sortOrder;

        GameObject box = new GameObject(objName, typeof(RectTransform), typeof(Image));
        box.transform.SetParent(root.transform, false);
        RectTransform rt = box.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 1f);
        rt.anchorMax = new Vector2(0.5f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = new Vector2(0f, y);
        rt.sizeDelta = new Vector2(Screen.width * 0.86f, height);

        Image bg = box.GetComponent<Image>();
        bg.color = new Color(0f, 0f, 0f, 0.75f);
        bg.raycastTarget = false;

        text = CreateText(objName + "Text", box.transform, 34f, FontStyles.Normal);
        RectTransform tt = text.rectTransform;
        tt.anchorMin = Vector2.zero;
        tt.anchorMax = Vector2.one;
        tt.offsetMin = new Vector2(20f, 10f);
        tt.offsetMax = new Vector2(-20f, -10f);
        text.alignment = TextAlignmentOptions.Center;
        text.enableAutoSizing = true;
        text.fontSizeMin = 22f;
        text.fontSizeMax = 36f;

        return box;
    }

    void ShowToast(string message)
    {
        if (toastBox == null) return;
        toastText.text = message;
        toastBox.SetActive(true);
        CancelInvoke(nameof(HideToast));
        Invoke(nameof(HideToast), 2.5f);
    }

    void HideToast()
    {
        if (toastBox != null) toastBox.SetActive(false);
    }

    // ---------- Foto ----------

    public void TakePhoto()
    {
        if (isCapturing) return;
        StartCoroutine(CapturePhoto());
    }

    IEnumerator CapturePhoto()
    {
        isCapturing = true;

        bool buttonsWereActive = buttonsCanvas != null && buttonsCanvas.activeSelf;
        bool panelWasActive = infoRoot != null && infoRoot.activeSelf;

        // sembunyikan semua yang tidak boleh masuk foto
        if (buttonsCanvas != null) buttonsCanvas.SetActive(false);
        if (!includeInfoPanel && infoRoot != null) infoRoot.SetActive(false);
        if (toastBox != null) toastBox.SetActive(false);
        if (hintBox != null) hintBox.SetActive(false);
        if (ghost != null) ghost.SetActive(false);

        yield return new WaitForEndOfFrame();

        Texture2D tex = ScreenCapture.CaptureScreenshotAsTexture();

        if (buttonsCanvas != null) buttonsCanvas.SetActive(buttonsWereActive);
        if (infoRoot != null) infoRoot.SetActive(panelWasActive);

        string fileName = "AR_" + System.DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".png";

#if UNITY_EDITOR
        string folder = Path.Combine(Application.dataPath, "../Photos");
        Directory.CreateDirectory(folder);
        File.WriteAllBytes(Path.Combine(folder, fileName), tex.EncodeToPNG());
        ShowToast("Foto tersimpan (editor): Photos/" + fileName);
#else
        NativeGallery.SaveImageToGallery(tex, albumName, fileName, (success, path) =>
        {
            ShowToast(success ? "Foto tersimpan di galeri" : "Gagal menyimpan foto");
        });
#endif

        Destroy(tex);
        isCapturing = false;
    }

    // ---------- Pilih dan kunci model ----------

    public void Next()
    {
        if (modelPrefabs.Length == 0) return;
        selectedIndex = (selectedIndex + 1) % modelPrefabs.Length;
        UpdateLabel();
        RebuildGhost();
    }

    public void Previous()
    {
        if (modelPrefabs.Length == 0) return;
        selectedIndex = (selectedIndex - 1 + modelPrefabs.Length) % modelPrefabs.Length;
        UpdateLabel();
        RebuildGhost();
    }

    public void ConfirmModel()
    {
        if (modelPrefabs.Length == 0) return;

        // model sudah terkunci: tombol tengah tidak memindahkannya lagi
        if (current != null)
        {
            ShowToast("Model sudah terkunci. Tekan X untuk memindahkan atau mengganti model.");
            return;
        }

        Vector3 pos;
        Quaternion rot;

        if (hasGhostPose)
        {
            // pakai posisi dan arah pratinjau biru
            pos = ghostPos;
            rot = ghostRot;
        }
        else
        {
            Transform cam = arCamera.transform;
            pos = cam.position + cam.forward * fallbackDistance;
            Vector3 dir = cam.position - pos;
            dir.y = 0;
            rot = dir != Vector3.zero ? Quaternion.LookRotation(dir) : Quaternion.identity;
        }

        current = Instantiate(modelPrefabs[selectedIndex], pos, rot);
        FreezeModel(current);
        LockToWorld(current, new Pose(pos, rot));

        if (ghost != null) ghost.SetActive(false);
        if (deleteButton != null) deleteButton.SetActive(true);
        if (photoButton != null) photoButton.SetActive(true);
        ShowInfo(selectedIndex);
        ShowToast("Model terkunci. Silakan berkeliling melihat modelnya.");
    }

    // Buat anchor di titik model, lalu jadikan model anak dari anchor itu
    async void LockToWorld(GameObject model, Pose pose)
    {
        if (anchorManager == null || !anchorManager.enabled) return;   // mis. di editor

        var result = await anchorManager.TryAddAnchorAsync(pose);
        if (!result.status.IsSuccess()) return;                        // model tetap diam di titiknya

        ARAnchor anchor = result.value;

        // model sudah dihapus sebelum anchor selesai dibuat
        if (model == null || current != model)
        {
            if (anchor != null) anchorManager.TryRemoveAnchor(anchor);
            return;
        }

        model.transform.SetParent(anchor.transform, true);   // posisi dunia tidak berubah
        currentAnchor = anchor;
    }

    public void HideModel()
    {
        // lepas model dari anchor, hapus anchor, lalu hapus model
        if (current != null) current.transform.SetParent(null, true);
        if (currentAnchor != null && anchorManager != null)
            anchorManager.TryRemoveAnchor(currentAnchor);
        currentAnchor = null;

        if (current != null) Destroy(current);
        current = null;

        if (deleteButton != null) deleteButton.SetActive(false);
        if (photoButton != null) photoButton.SetActive(false);
        if (infoRoot != null) infoRoot.SetActive(false);
    }

    void ShowInfo(int index)
    {
        if (infoRoot == null) return;

        infoTitleText.text = GetName(index);
        infoBodyText.text = (index < histories.Length) ? histories[index] : "";

        infoRoot.SetActive(true);
        PositionInfoPanel();
    }

    // Taruh panel di dekat model, menurut posisi model di layar
    void PositionInfoPanel()
    {
        if (current == null || infoBox == null || arCamera == null) return;

        float w = Screen.width * panelWidthPercent;
        float h = Screen.height * panelHeightPercent;
        infoBox.sizeDelta = new Vector2(w, h);

        Renderer[] rs = current.GetComponentsInChildren<Renderer>();
        if (rs.Length == 0) return;

        Bounds b = rs[0].bounds;
        for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds);

        Vector2 min = new Vector2(float.MaxValue, float.MaxValue);
        Vector2 max = new Vector2(float.MinValue, float.MinValue);
        bool anyVisible = false;
        Vector3 ext = b.extents;

        for (int i = 0; i < 8; i++)
        {
            Vector3 corner = b.center + new Vector3(
                (i & 1) == 0 ? -ext.x : ext.x,
                (i & 2) == 0 ? -ext.y : ext.y,
                (i & 4) == 0 ? -ext.z : ext.z);

            Vector3 sp = arCamera.WorldToScreenPoint(corner);
            if (sp.z <= 0f) continue;

            anyVisible = true;
            min = Vector2.Min(min, new Vector2(sp.x, sp.y));
            max = Vector2.Max(max, new Vector2(sp.x, sp.y));
        }

        if (!anyVisible) return;

        float margin = 20f;
        float bottom = Screen.height * bottomReservePercent;
        float cx = (min.x + max.x) / 2f;
        float cy = (min.y + max.y) / 2f;
        float x = cx;
        float y;

        if (max.y + panelGap + h <= Screen.height - margin)
        {
            y = max.y + panelGap + h / 2f;             // di atas model
        }
        else if (min.y - panelGap - h >= bottom)
        {
            y = min.y - panelGap - h / 2f;             // di bawah model
        }
        else
        {
            y = cy;                                    // di samping model
            x = (max.x + panelGap + w <= Screen.width - margin)
                ? max.x + panelGap + w / 2f
                : min.x - panelGap - w / 2f;
        }

        x = Mathf.Clamp(x, w / 2f + margin, Screen.width - w / 2f - margin);
        y = Mathf.Clamp(y, bottom + h / 2f, Screen.height - h / 2f - margin);

        infoBox.anchoredPosition = new Vector2(x, y);
    }

    void Update()
    {
        if (arCamera != null) UpdateGhost();
    }

    void LateUpdate()
    {
        if (infoRoot != null && infoRoot.activeSelf)
            PositionInfoPanel();
    }

    string GetName(int index)
    {
        return (index < modelNames.Length && !string.IsNullOrEmpty(modelNames[index]))
            ? modelNames[index]
            : "Model " + (index + 1);
    }

    void UpdateLabel()
    {
        if (labelText == null) return;
        labelText.text = GetName(selectedIndex);
    }
}