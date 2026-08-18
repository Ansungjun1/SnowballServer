using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class PlayerState : MonoBehaviour
{
    public Slider hpSlider;
    public TextMeshProUGUI nameText;
    public float MaxHp { get; private set; }
    public float CurrentHp { get; private set; }

    public bool IsDead { get; private set; }

    private PlayerMovement movement;

    private void Awake()
    {
        movement = GetComponent<PlayerMovement>();
    }

    public void SetHp(float hp)
    {
        CurrentHp = Mathf.Clamp(hp, 0f, MaxHp);
        hpSlider.value = CurrentHp / MaxHp;

        if (CurrentHp <= 0f && !IsDead)
        {
            IsDead = true;
            movement.PlayDeath();
        }
    }

    public void PlayHit()
    {
        if (IsDead)
            return;

        movement.PlayHit();
    }

    private void OnTriggerEnter(Collider other)
    {
        SnowItem snowItem = other.GetComponent<SnowItem>();

        if (snowItem == null)
            return;

        FindObjectOfType<NetworkClient>().RequestSnowItem(snowItem.itemId);
    }
}
