using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace autodeflect
{
    public class ModBehaviour : Duckov.Modding.ModBehaviour
    {
        protected override void OnAfterSetup()
        {
            try
            {
                Debug.Log("[AutoDeflect] OnAfterSetup - Manager 생성");
                GameObject root = new GameObject("AutoDeflectRoot");
                UnityEngine.Object.DontDestroyOnLoad(root);
                root.AddComponent<AutoDeflectManager>();
            }
            catch (Exception ex)
            {
                Debug.Log("[AutoDeflect] 예외: " + ex);
            }
        }

        protected override void OnBeforeDeactivate()
        {
            Debug.Log("[AutoDeflect] 언로드");
        }
    }

    public class AutoDeflectManager : MonoBehaviour
    {
        private static AutoDeflectManager _instance;

        private float _scanInterval = 0.02f;
        private float _nextScanTime = 0f;
        private float _autoRadius = 5.0f;

        private float _deflectChance = 1.0f; // 100% 확률

        private Vector3 _playerPos;

        private CharacterMainControl _localPlayer;
        private FieldInfo _fiWeapon;
        private Type _bulletType;
        private FieldInfo _fiBulletOwner;
        private FieldInfo _fiBulletVelocity;

        // Health kill용 캐시
        private FieldInfo _fiCharHealth;
        private FieldInfo _fiCurrentHP;
        private MethodInfo _miKill;

        void Awake()
        {
            if (_instance != null)
            {
                Destroy(this);
                return;
            }
            _instance = this;
        }

        IEnumerator Start()
        {
            Debug.Log("[AutoDeflect] Start");

            while (_localPlayer == null)
            {
                _localPlayer = FindObjectOfType<CharacterMainControl>();
                yield return new WaitForSeconds(0.5f);
            }

            _fiWeapon = typeof(CharacterMainControl).GetField("equippedItem",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            _bulletType = AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(s => s.GetTypes())
                .FirstOrDefault(t => t.Name == "Bullet");

            if (_bulletType != null)
            {
                _fiBulletOwner = _bulletType.GetField("Owner",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

                _fiBulletVelocity = _bulletType.GetField("velocity",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            }

            CacheHealthReflection();

            Debug.Log("[AutoDeflect] 리플렉션 준비 완료");
        }

        private void CacheHealthReflection()
        {
            _fiCharHealth = typeof(CharacterMainControl).GetField("health",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            if (_fiCharHealth == null) return;

            var healthObj = _fiCharHealth.GetValue(_localPlayer);
            if (healthObj == null) return;

            Type healthType = healthObj.GetType();

            _fiCurrentHP = healthType.GetField("CurrentHealth",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            _miKill = healthType.GetMethod("Kill",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        }

        void Update()
        {
            if (_localPlayer == null) return;
            if (Time.time < _nextScanTime) return;

            _nextScanTime = Time.time + _scanInterval;
            if (!IsMeleeWeaponEquipped()) return;

            _playerPos = _localPlayer.transform.position;

            var bullets = FindObjectsOfType<MonoBehaviour>()
                .Where(b => b.GetType() == _bulletType)
                .ToList();

            foreach (var b in bullets)
            {
                TryAutoDeflect(b);
            }
        }

        private bool IsMeleeWeaponEquipped()
        {
            if (_fiWeapon == null) return false;

            object weaponObj = _fiWeapon.GetValue(_localPlayer);
            if (weaponObj == null) return false;

            var fiItemData = weaponObj.GetType().GetField("itemData",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (fiItemData == null) return false;

            var itemData = fiItemData.GetValue(weaponObj);
            if (itemData == null) return false;

            var fiCategory = itemData.GetType().GetField("category",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (fiCategory == null) return false;

            string category = fiCategory.GetValue(itemData) as string;
            if (category == null) return false;

            return category.ToLower().Contains("melee");
        }

        private void TryAutoDeflect(MonoBehaviour bullet)
        {
            Vector3 bpos = bullet.transform.position;
            float dist = Vector3.Distance(_playerPos, bpos);
            if (dist > _autoRadius) return;

            if (_fiBulletOwner != null)
            {
                var owner = _fiBulletOwner.GetValue(bullet);
                if (owner == _localPlayer) return;
            }

            DeflectBullet(bullet);
        }

        private void DeflectBullet(MonoBehaviour bullet)
        {
            if (_fiBulletVelocity == null) return;

            Vector3 v = (Vector3)_fiBulletVelocity.GetValue(bullet);
            Vector3 newV = -v.normalized * v.magnitude;

            _fiBulletVelocity.SetValue(bullet, newV);

            // ✔ 핵심: 반사된 총알 Owner를 플레이어로 변경
            if (_fiBulletOwner != null)
                _fiBulletOwner.SetValue(bullet, _localPlayer);

            CreateDeflectEffect(bullet.transform.position);

            Debug.Log("[AutoDeflect] 총알 자동 튕겨냄 (Owner = Player)");
        }

        private void CreateDeflectEffect(Vector3 pos)
        {
            GameObject fx = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            fx.transform.position = pos;
            fx.transform.localScale = Vector3.one * 0.15f;
            fx.GetComponent<MeshRenderer>().material.color = Color.yellow;
            Destroy(fx, 0.2f);
        }

        // ✔ 필요할 때 강제 Kill (백업용)
        private void ForceKill(CharacterMainControl enemy)
        {
            if (_fiCharHealth == null || _fiCurrentHP == null || _miKill == null)
                return;

            var health = _fiCharHealth.GetValue(enemy);
            if (health == null) return;

            float hp = (float)_fiCurrentHP.GetValue(health);
            if (hp <= 0f)
            {
                _miKill.Invoke(health, null);
                Debug.Log("[AutoDeflect] 강제 Kill()");
            }
        }
    }
}
