using RandomCar.Car;

namespace RandomCar.EditorTools
{
    /// Список машин игры для сборщиков (префабы, характеристики, каталог, лаборатория физики).
    /// Модель каждой машины — Assets/Art/Models/Cars/Car_{Key}.fbx из Tools/Blender (car_designs.py).
    public static class CarRoster
    {
        public sealed class Entry
        {
            public string Key;
            public string Name;
            public CarClass Class;
            public int Price;
            public string Unlock;
            public string Paint;

            public Entry(string key, string name, CarClass cls, int price, string unlock, string paint)
            {
                Key = key;
                Name = name;
                Class = cls;
                Price = price;
                Unlock = unlock;
                Paint = paint;
            }
        }

        /// Порядок — порядок в гараже. Цена 0 — машина доступна сразу.
        public static readonly Entry[] Cars =
        {
            new Entry("Iskra", "Искра", CarClass.Sport, 0, "", "#E2401F"),
            new Entry("Kometa", "Комета", CarClass.Sedan, 0, "", "#1F4FD8"),
            new Entry("Pchelka", "Пчёлка", CarClass.Rally, 0, "", "#FFC21A"),
            new Entry("Myata", "Мята", CarClass.Compact, 0, "", "#2DD4A0"),
            new Entry("Bastion", "Бастион", CarClass.LuxurySuv, 42000, "", "#1A1C20"),
            new Entry("Bizon", "Бизон", CarClass.Muscle, 30000, "", "#F2B21A"),
            new Entry("Taiga", "Тайга", CarClass.Offroad, 36000, "", "#5E6B3A"),
            new Entry("Lastochka", "Ласточка", CarClass.Classic, 25000, "wins:3", "#7FB8D8"),
            new Entry("Grom", "Гром", CarClass.Super, 85000, "races:5", "#A6E23A"),
            new Entry("Sapsan", "Сапсан", CarClass.Hyper, 140000, "wins:5", "#F2F0E8"),
        };

        public static string[] Keys
        {
            get
            {
                var keys = new string[Cars.Length];
                for (int i = 0; i < Cars.Length; i++) keys[i] = Cars[i].Key;
                return keys;
            }
        }

        /// Машина уже переведена на модель генератора v2 (салон, тормоза, фары-накладки).
        public static bool IsV2(string key) => true;
    }
}
