using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.TextCore.Text;

public class Loding : MonoBehaviour
{
    public LodingManager LM;
    public GameObject Chatting;
    public void OnPlay()
    {
        LM.SetPlay();

        Chatting.SetActive(true);
    }
}
