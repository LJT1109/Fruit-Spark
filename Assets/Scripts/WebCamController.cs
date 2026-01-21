using UnityEngine;
using UnityEngine.UI;

public class WebCamController : MonoBehaviour
{
    [Header("UI Settings")]
    [SerializeField] private RawImage displayImage;

    [Header("WebCam Settings")]
    [SerializeField] private int requestedWidth = 1920;
    [SerializeField] private int requestedHeight = 1080;
    [SerializeField] private int requestedFPS = 30;

    private WebCamTexture webCamTexture;

    void Start()
    {
        // 檢查是否有攝影機裝置
        if (WebCamTexture.devices.Length == 0)
        {
            Debug.LogError("找不到攝影機裝置！");
            return;
        }

        // 選擇第一個可用的裝置
        WebCamDevice device = WebCamTexture.devices[0];
        Debug.Log($"使用攝影機: {device.name}");

        // 初始化 WebCamTexture
        webCamTexture = new WebCamTexture(device.name, requestedWidth, requestedHeight, requestedFPS);

        // 如果有指派 RawImage，將攝影機畫面顯示在上面
        if (displayImage != null)
        {
            displayImage.texture = webCamTexture;
            
            // 修正鏡像問題（如果使用的是前置鏡頭，通常需要鏡像，但在 PC 或是 WebCam 通常不需要，視需求調整）
            // 這裡保持原始方向，或是可以根據需要調整 displayImage.rectTransform.localScale
        }
        else
        {
            Debug.LogWarning("未指派 RawImage，攝影機畫面將無法顯示在 UI 上。");
        }

        // 開始播放
        webCamTexture.Play();
    }

    void OnDestroy()
    {
        // 確保在腳本銷毀或停止時關閉攝影機
        if (webCamTexture != null)
        {
            webCamTexture.Stop();
        }
    }
}
