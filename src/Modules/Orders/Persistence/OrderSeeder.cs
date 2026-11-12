using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Bogus;
using EFCore.BulkExtensions;
using Microsoft.EntityFrameworkCore;
using Modules.Orders.Entities;
// ملاحظة: تأكد من إضافة مسارات الأسماء (usings) الصحيحة للكلاسات الخاصة بك
// مثلاً: using Modules.Orders.Entities;

namespace Modules.Orders.Persistence
{
    public static class OrderSeeder
    {
        // استبدل 'OrderDbContext' باسم DbContext الخاص بالطلبات لديك إذا كان مختلفاً
        public static async Task SeedOrdersAsync(OrdersDbContext context)
        {
            // التحقق مما إذا كانت البيانات موجودة مسبقاً لتجنب التكرار
            if (await context.Orders.AnyAsync()) return; 

            // استخدام الـ IDs الحقيقية من ملف Postman الخاص بك
            var customerId = Guid.Parse("2892a47f-6a0e-4038-babe-b7afdcd78929"); //[cite: 2]
            var productId = Guid.Parse("35158913-13fe-4177-b434-620d06ddd74b"); //[cite: 2]

            // إعداد Bogus لتوليد الطلبات
            var orderFaker = new Faker<Order>()
        .RuleFor(o => o.Id, f => Guid.NewGuid())
        .RuleFor(o => o.UserId, f => customerId) // تم تغيير CustomerId إلى UserId
        .RuleFor(o => o.Status, f => f.PickRandom<OrderStatus>()) // استخدام الـ Enum الخاص بالحالة
        .RuleFor(o => o.CreatedAt, f => f.Date.Past(1).ToUniversalTime());

            // توليد 200,000 طلب في الذاكرة
            var orders = orderFaker.Generate(200_000); 
            var orderItems = new List<OrderItem>(); // تأكد أن كلاس OrderItem مطابق لديك

            var itemFaker = new Faker<OrderItem>()
                .RuleFor(i => i.Id, f => Guid.NewGuid())
                .RuleFor(i => i.ProductId, f => productId)
                .RuleFor(i => i.Quantity, f => f.Random.Number(1, 5));

            // ربط العناصر بالطلبات (عنصرين لكل طلب)
            foreach (var order in orders)
            {
                var items = itemFaker.Generate(2); 
                foreach (var item in items)
                {
                    item.OrderId = order.Id;
                    orderItems.Add(item);
                }
            }

            // الإدراج السريع في قاعدة البيانات دفعة واحدة
            await context.BulkInsertAsync(orders);
            await context.BulkInsertAsync(orderItems);
        }
    }
}