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

    public GameObject gunObject;
    private void Awake()
    {
        movement = GetComponent<PlayerMovement>();
        MaxHp = 5;
        CurrentHp = MaxHp;
        SetHp(CurrentHp);

        gunObject.SetActive(false);
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

    public void ResetDeath()
    {
        IsDead = false;
        movement.PlayThrow();
    }

    private void OnTriggerEnter(Collider other)
    {
        SnowItem snowItem = other.GetComponent<SnowItem>();
        if (snowItem != null)
        {
            FindObjectOfType<NetworkClient>().RequestSnowItem(snowItem.ownerId);
        }

        DroppedSnowItem droppedSnowItem = other.GetComponent<DroppedSnowItem>();
        if (droppedSnowItem != null)
        {
            FindObjectOfType<NetworkClient>().RequestDroppedSnowItem(droppedSnowItem.itemId);
        }

        if (other.tag == "Shop")
        {
            FindObjectOfType<NetworkClient>().RequestGunPurchase();
        }

        StorageState storage = other.GetComponent<StorageState>();

        if (storage != null)
        {
            FindObjectOfType<NetworkClient>().RequestStorageJoin(storage.ownerId);
        }

        if (other.tag == "Central")
        {
            FindObjectOfType<NetworkClient>().RequestCentralSnowball();
        }

        if (other.tag == "Bridge")
        {
            FindObjectOfType<NetworkClient>().RequestPurchaseBridge();
        }

        CoreState core = other.GetComponent<CoreState>();
        if (core != null)
        {
            FindObjectOfType<NetworkClient>().RequestCoreHit(core.ownerId, core.baseKey);
        }
    }
}
