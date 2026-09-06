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

    public bool isLocalPlayer = false;

    private NetworkClient networkClient;

    private void Awake()
    {
        networkClient = FindObjectOfType<NetworkClient>();
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
        if (!isLocalPlayer)
            return;

        SnowItem snowItem = other.GetComponent<SnowItem>();
        if (snowItem != null)
        {
            networkClient.RequestSnowItem(snowItem.ownerId);
        }

        DroppedSnowItem droppedSnowItem = other.GetComponent<DroppedSnowItem>();
        if (droppedSnowItem != null)
        {
            networkClient.RequestDroppedSnowItem(droppedSnowItem.itemId);
        }

        if (other.tag == "Shop")
        {
            networkClient.RequestGunPurchase();
        }

        StorageState storage = other.GetComponent<StorageState>();

        if (storage != null)
        {
            networkClient.RequestStorageJoin(storage.ownerId);
        }

        if (other.tag == "Central")
        {
            networkClient.RequestCentralSnowball();
        }

        if (other.tag == "Bridge")
        {
            BridgeState bridge =
                other.transform.parent
                    .GetComponentInChildren<BridgeState>(true);

            if (bridge == null)
                return;

            networkClient.RequestPurchaseBridge(bridge.bridgeKey);
        }

        CoreState core = other.GetComponent<CoreState>();
        if (core != null)
        {
            networkClient.RequestCoreHit(core.ownerId, core.baseKey);
        }

        if (other.tag == "Fall")
        {
            networkClient.DeadPlayer();
        }
    }
}
