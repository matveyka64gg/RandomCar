using RandomCar.Save;
using UnityEditor;
using UnityEngine;

namespace RandomCar.EditorTools
{
    /// Тестовые кредиты для гаража. Только редактор: в сборку игры не попадает.
    /// Работает и в Play Mode (экран гаража обновится сам через GarageStore.Changed), и вне его — пишет garage.json.
    static class DevCredits
    {
        const int Lots = 999_999_999;

        [MenuItem("RandomCar/Dev/+2 000 000 кредитов")]
        static void AddTwoMillion() => Add(2_000_000);

        [MenuItem("RandomCar/Dev/Кредиты: 999 999 999")]
        static void SetLots()
        {
            if (GarageStore.Credits < Lots) Add(Lots - GarageStore.Credits);
            else Debug.Log($"[RandomCar] Кредитов уже {GarageStore.Credits:N0}");
        }

        static void Add(int amount)
        {
            GarageStore.AddCredits(amount);
            Debug.Log($"[RandomCar] Тестовые кредиты: +{amount:N0}, теперь {GarageStore.Credits:N0}");
        }
    }
}
