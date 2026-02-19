using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;

public class flowGift : MonoBehaviour
{
    public Sprite yjs;
    private bool isYjs = false;
    enum MoveType
    {
        WaveLeft,
        Ufo,
        PulseDrift
    }

    MoveType moveType;

    float waveOffset;

    bool ufoStopped;
    float ufoTimer;

    float ufoVy;
    float ufoVx;

    void Start()
    {

        if (IsYjsStore.isYjsMode)
        {
            var image = GetComponent<Image>();
            image.sprite = yjs;
            isYjs = true;
        }

        EventTrigger trigger = gameObject.AddComponent<EventTrigger>();

        EventTrigger.Entry entry = new EventTrigger.Entry
        {
            eventID = EventTriggerType.PointerUp
        };
        entry.callback.AddListener((data) => pushButton());
        trigger.triggers.Add(entry);

        moveType = (MoveType)Random.Range(0, 3);

        waveOffset = Random.Range(0f, 10f);
        ufoStopped = true;
        ufoTimer = 0f;

        ufoVy = 0f;
        ufoVx = -120f;
    }

    void Update()
    {
        float dt = Time.deltaTime;
        waveOffset += dt;

        switch (moveType)
        {
            case MoveType.WaveLeft:
                {
                    Vector2 velocity = new Vector2(-60f, Mathf.Cos(waveOffset) * 80f);
                    transform.Translate(velocity * dt, Space.Self);
                    break;
                }

            case MoveType.Ufo:
                {
                    ufoTimer += dt;

                    if (ufoStopped)
                    {
                        if (ufoTimer >= 1.5f)
                        {
                            ufoStopped = false;
                            ufoTimer = 0f;

                            float y = transform.localPosition.y;

                            float thetaDeg = Random.Range(20f, 80f);
                            float speed = Random.Range(100.0f, 4000.0f);
                            if (y > 0f) thetaDeg = -thetaDeg;

                            float thetaRad = thetaDeg * Mathf.Deg2Rad;

                            ufoVx = -Mathf.Cos(thetaRad) * speed;
                            ufoVy = Mathf.Sin(thetaRad) * speed;
                        }
                    }
                    else
                    {
                        transform.Translate(new Vector2(ufoVx, ufoVy) * dt, Space.Self);

                        if (ufoTimer >= 0.2f)
                        {
                            ufoStopped = true;
                            ufoTimer = 0f;
                        }
                    }

                    break;
                }

            case MoveType.PulseDrift:
                {
                    float dx = Time.deltaTime;

                    transform.Translate(Vector2.left * 400f * dx, Space.World);
                    transform.Rotate(0f, 0f, -1800f * dx, Space.Self);

                    break;
                }
        }

        if (transform.localPosition.x < -500f)
        {
            Destroy(gameObject);
        }
    }

    void pushButton()
    {
        var gen = GetComponentInParent<flowGiftGen>();
        if (gen != null)
        {
            gen.ShowToast(transform.localPosition, isYjs);
        }

        Destroy(gameObject);
    }
}
