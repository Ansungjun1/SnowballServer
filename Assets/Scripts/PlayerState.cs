using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class PlayerState : MonoBehaviour
{
    public Slider hpSlider;
    public float MaxHp { get; private set; }
    public float CurrentHp { get; private set; }

    public bool isDead = false;
    public TextMeshProUGUI nameText;

    private void Start()
    {
        MaxHp = 3f;
        CurrentHp = MaxHp;

        if (FindObjectOfType<NetworkClient>().character != gameObject)
            Destroy(this.GetComponent<PlayerState>());
    }

    private void OnTriggerEnter2D(Collider2D collision)
    {
        if(collision.tag == "Bullet" && !isDead)
        {
            CurrentHp -= 1;

            hpSlider.value = CurrentHp / MaxHp;

            if(CurrentHp <= 0f)
            {
                isDead = true;

                FindObjectOfType<Dead>().deadAni.gameObject.SetActive(true);
            }
        }
    }
}
