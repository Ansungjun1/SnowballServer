using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;

public class LodingManager : MonoBehaviour
{
    public GameObject LoadImage;
    public GameObject HomeScene;
    public GameObject NameScene;
    public GameObject WaitImage;
    public TextMeshProUGUI Name_Text;

    public TextMeshProUGUI Home_Name;
    public TextMeshProUGUI Rank_Name;

    public GameObject RankingImage;
    public GameObject SettingImage;
    public string Name { get; set; }
    private void Start()
    {
        Application.runInBackground = true;
    }
    public void OffLoadImage()
    {
        LoadImage.SetActive(false);
        NameScene.SetActive(true);
    }

    public void OffNameImage()
    {
        NameScene.SetActive(false);
        HomeScene.SetActive(true);

        Name = Name_Text.text;
        Home_Name.text = Name;
        Rank_Name.text = Name;
    }

    public void SetRanking(bool isOn)
    {
        RankingImage.SetActive(isOn);
    }
    public void SetSetting(bool isOn)
    {
        SettingImage.SetActive(isOn);
    }
    public void SetHome()
    {
        HomeScene.SetActive(false);
        WaitImage.SetActive(true);


        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    public void SetPlay()
    {
        WaitImage.SetActive(false);
    }
    public void ReturnRobby()
    {
        HomeScene.SetActive(true);

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }
}
