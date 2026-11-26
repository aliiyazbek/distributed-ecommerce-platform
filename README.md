# منصة تجارة إلكترونية موزّعة ولحظية

**Distributed Real-Time E-Commerce Platform**

مشروع مادة تطبيقات الإنترنت · جامعة دمشق · كلية الهندسة المعلوماتية · 2026

## فكرة المشروع

منصة تجارة إلكترونية صغيرة تعمل كما تعمل الأنظمة الحقيقية. التركيز على الأمان والأداء والتوسع والمراقبة، وليس على كثرة الوظائف أو تصميم الواجهة.

الوظائف الأساسية: تسجيل المستخدمين، إضافة المنتجات مع صورها وعرضها، إنشاء الطلبات، إدارة المخزون، متابعة حالة الطلب.

## المتطلبات

| # | المتطلب | الحالة |
|---|---|---|
| 1 | تصميم الواجهات البرمجية والأمان (API Design & Security) | قيد العمل |
| 2 | التواصل بين الخدمات (gRPC) | لم يبدأ |
| 3 | الأداء والتخزين المؤقت | لم يبدأ |
| 4 | المعالجة غير المتزامنة واللحظية | منجز (الطوابير وعامل البريد، والتحديثات اللحظية مع الصلاحيات) |
| 5 | التوسع وتوزيع الأحمال | لم يبدأ |
| 6 | المراقبة وتتبع الأعطال | لم يبدأ |

## الفريق

| الاسم | حساب GitHub |
|---|---|
| | |
| | |
| | |
| | |

## التشغيل

يُضاف بعد اختيار اللغة والإطار. سيعمل المشروع كاملاً باستخدام Docker بأمر واحد:

```bash
docker compose up
```

## المتطلب الرابع: الطوابير غير المتزامنة وعامل البريد

عند إنشاء طلب يُنشر حدث في وسيط الرسائل RabbitMQ، ويردّ التطبيق على الزبون فوراً دون انتظار إرسال البريد. عاملٌ مستقل (Worker) يقرأ الحدث ويرسل بريد تأكيد حقيقياً، ولا تضيع أي رسالة حتى لو توقّف العامل.

> هذا القسم يغطي جزء الطوابير والبريد. صفحة متابعة الطلب اللحظية ولوحة المسؤول والتحقق من الصلاحيات قبل الاشتراك في القسم التالي.

### البنية

```
الزبون ──POST /api/v1/orders──► API
                                 │  معاملة واحدة في قاعدة البيانات:
                                 │  صف الطلب + صف في orders."OutboxMessages"
                                 ▼
                       OutboxPublisher (خدمة خلفية داخل الـ API)
                                 │  نشر + تأكيد من RabbitMQ (publisher confirm)، ثم حذف الصف
                                 ▼
                   exchange: orders.events (topic)
             ┌───────────────────┴────────────────────┐
     order.created                          order.created / order.status-changed
             ▼                                         ▼
  queue: email.order-created                 queue: realtime.order-events (القسم التالي)
             ▼
      EmailWorker ── فشل مؤقت ──► email.order-created.retry  (انتظار 10 ثوانٍ ثم العودة)
             │              بعد 3 محاولات أو فشل دائم ──► email.order-created.dlq
             ▼
       SMTP (Gmail أو Mailpit)
```

| المكوّن | المكان | الدور |
|---|---|---|
| عقد الرسائل | `src/Shared/Messaging` | أسماء الـ exchange ومفاتيح التوجيه، الحدثان `OrderCreatedEvent` و`OrderStatusChangedEvent`، وتعريف الطوابير المشترك `RabbitMqTopology` |
| صندوق الصادر (Transactional Outbox) | `src/Modules/Orders/Persistence/Outbox` | يُحفظ الحدث في نفس معاملة الطلب، فلا يضيع بين «حُفظ الطلب» و«أُرسلت الرسالة» |
| ناشر الصادر | `OutboxPublisher.cs` | يوقَظ فور الحفظ (مع فحص دوري كل ثانيتين احتياطاً)، ينشر مع تأكيد الوسيط، ثم يحذف الصف |
| عامل البريد | `src/Workers/EmailWorker` | عملية مستقلة عن الـ API: إقرار يدوي، إعادة المحاولة، طابور الرسائل الميتة، ومنع التكرار |

### لماذا لا تضيع أي رسالة

| الحالة | ما يحدث |
|---|---|
| RabbitMQ متوقف لحظة إنشاء الطلب | الطلب يُنشأ بنجاح والحدث ينتظر في جدول `OutboxMessages`، ثم يُنشر تلقائياً عند عودة RabbitMQ |
| العامل متوقف أو لم يُشغَّل بعد | الطابور `email.order-created` دائم (durable) والرسائل دائمة (persistent)، فتنتظر حتى يعود العامل. الطوابير تُعرَّف من الـ API أيضاً، فلا تُرمى رسائل لعدم وجود طابور |
| تعطّل العامل أثناء الإرسال | الإقرار يدوي (manual ack): لا تُحذف الرسالة من الطابور إلا بعد إرسال البريد، وإلا يعيد RabbitMQ تسليمها |
| فشل مؤقت (خادم البريد متوقف، مهلة، رمز 4xx) | تُرفض الرسالة فتنتقل إلى `email.order-created.retry`، تنتظر 10 ثوانٍ ثم تعود. عدد المحاولات من ترويسة `x-death` |
| فشل دائم (رمز 5xx، عنوان غير صالح) أو فشل المحاولة الثالثة | تُنسخ إلى `email.order-created.dlq` مع سبب الفشل في الترويسات (`x-failure-reason`, `x-failed-attempts`) بعد تأكيد الوسيط، ثم تُحذف من الطابور الرئيسي |
| وصول الرسالة نفسها مرتين | يُسجَّل `message_id` في جدول `email."ProcessedMessages"`، فلا يُرسل بريد ثانٍ |

الضمان هو التسليم مرة واحدة على الأقل (at-least-once): قد يتكرر بريدٌ واحد إذا تعطّل العامل في اللحظة بين الإرسال وتسجيل الرسالة، لأن إرسال البريد وقاعدة البيانات لا يجمعهما معاملة واحدة، لكن لا تضيع أي رسالة.

### التشغيل

```bash
docker compose up -d        # تشمل الآن rabbitmq (لوحة التحكم: http://localhost:15672 ، المستخدم ecommerce / ecommerce_password)

# الجداول الجديدة (مرة واحدة)
dotnet ef database update --project src/Modules/Orders --startup-project src/Host --context OrdersDbContext
dotnet ef database update --project src/Workers/EmailWorker --context EmailWorkerDbContext

node inventory-service/server.js
dotnet run --project src/Host
dotnet run --project src/Workers/EmailWorker     # عامل البريد: عملية مستقلة
```

افتراضياً يرسل العامل إلى **Mailpit** (صندوق بريد محلي للتجربة: http://localhost:8025)، فلا تُرسل رسائل حقيقية أثناء التطوير.

### الإرسال الحقيقي عبر Gmail

1. فعّل التحقق بخطوتين في حساب Google، ثم أنشئ «كلمة مرور تطبيق» (App Password): https://myaccount.google.com/apppasswords
2. خزّن الإعدادات في **user-secrets** (خارج المستودع، لا تُرفع إلى GitHub أبداً):

```bash
cd src/Workers/EmailWorker
dotnet user-secrets set "Email:Host" "smtp.gmail.com"
dotnet user-secrets set "Email:Port" "587"
dotnet user-secrets set "Email:Security" "StartTls"
dotnet user-secrets set "Email:UserName" "your.address@gmail.com"
dotnet user-secrets set "Email:FromAddress" "your.address@gmail.com"
dotnet user-secrets set "Email:Password" "<app password>"
```

عند التشغيل يطبع العامل الخادم المستخدم (دون كلمة المرور)، مثلاً: `Sending emails via smtp.gmail.com:587 (StartTls)`. للعودة إلى Mailpit: `dotnet user-secrets clear`.

> البريد يُرسل إلى عنوان الزبون المسجّل في التوكن، لذلك سجّل للعرض حساب زبون ببريد حقيقي تستطيع فتحه.

### سيناريو العرض (المناقشة)

**1. إيقاف العامل لا يضيّع أي بريد**

1. أوقف العامل (`Ctrl+C`).
2. أنشئ طلبين أو ثلاثة من المتجر. الـ API يرد فوراً `201`.
3. في لوحة RabbitMQ ← **Queues**: الطابور `email.order-created` يحتوي الرسائل تنتظر.
4. شغّل العامل: تُرسل كل الرسائل ويصبح الطابور فارغاً.

**2. إعادة المحاولة ثم طابور الرسائل الميتة (DLQ)**

```bash
cd src/Workers/EmailWorker
Email__SimulateFailure=true dotnet run       # كل إرسال يفشل فشلاً مؤقتاً
```

أنشئ طلباً وراقب السجل: `attempt 1/3` ← انتظار 10 ثوانٍ ← `attempt 2/3` ← `attempt 3/3` ← `Moving to the DLQ`. في لوحة RabbitMQ تظهر الرسالة في `email.order-created.dlq`، ومن **Get messages** تظهر الترويسات `x-failure-reason` و`x-failed-attempts`. بديل واقعي: أوقف Mailpit (`docker compose stop mailpit`) بدل المفتاح.

**3. توقّف RabbitMQ نفسه**

`docker compose stop rabbitmq`، ثم أنشئ طلباً: الطلب ينجح والحدث يبقى في جدول `orders."OutboxMessages"`. بعد `docker compose start rabbitmq` يُنشر تلقائياً ويصل البريد.

## المتطلب الرابع: التحديثات اللحظية والصلاحيات

تتحدّث صفحة متابعة الطلب ولوحة المسؤول لحظياً دون تحديث الصفحة: يظهر الطلب الجديد فوراً في لوحة المسؤول، وتظهر موافقة المسؤول (وأي تغيير في الحالة) فوراً في صفحة الزبون. ولا يُفتح أي اتصال قبل التحقق من هوية المستخدم وصلاحيته.

### البنية

```
exchange: orders.events ──order.created / order.status-changed──► queue: realtime.order-events
                                                                          │
                                                        OrderEventsConsumer (خدمة خلفية داخل الـ API)
                                            ┌─────────────────────────────┴─────────────────────────────┐
                                     order.created                                         order.status-changed
                                            ▼                                                           ▼
                       /hubs/admin-orders  (المسؤول فقط)                             /hubs/orders  (الزبون فقط)
                       إلى كل المسؤولين: OrderCreated                          إلى صاحب الطلب فقط: OrderStatusChanged
                                            ▼                                                           ▼
                       صفحة All orders: يظهر الطلب في الأعلى                   صفحة الطلب: الحالة وشريط التقدّم
```

| المكوّن | المكان | الدور |
|---|---|---|
| قناتا SignalR | `src/Modules/Notifications/Realtime/OrdersHub.cs` و`AdminOrdersHub.cs` | اتصال WebSocket عبر SignalR. لا تحتوي القناتان أي دالة: المتصفح لا يطلب شيئاً، والخادم وحده يرسل |
| المستهلك | `src/Modules/Notifications/Realtime/OrderEventsConsumer.cs` | يقرأ طابوره الخاص، ويرسل كل حدث إلى القناة المناسبة، ثم يُقرّ بالرسالة |
| التوكن في الرابط | `src/Host/Program.cs` | المتصفح لا يرسل ترويسات مع WebSocket، فيُقبل `access_token` من الرابط لمسارات `/hubs` فقط |
| الواجهة | `frontend/ecommerce-store/src/services/realtime.js` | الاتصال وإعادة الاتصال التلقائية، وتستخدمه صفحة الطلب وصفحة All orders |
| حالة «موافق عليه» | `OrderStatus.Approved` | حالة جديدة بين `Pending` و`Shipped`، تُخزَّن نصاً فلا تحتاج migration |

### الصلاحيات قبل الاشتراك

يجري الفحص على الخادم عند فتح الاتصال، قبل أي اشتراك:

| الحالة | النتيجة |
|---|---|
| بدون توكن، أو بتوكن معدَّل، أو منتهي الصلاحية | `401` ولا يُفتح الاتصال |
| زبون يفتح قناة المسؤول، أو مسؤول يفتح قناة الزبون | `403` |
| انتهاء صلاحية التوكن أثناء الاتصال | يغلق الخادم الاتصال تلقائياً (`CloseOnAuthenticationExpiration`) |
| زبون آخر | لا يصله أي حدث: يُرسل الحدث إلى صاحب الطلب فقط (`Clients.User`) حسب هويته في التوكن الموقّع، ولا توجد دالة يطلب بها المتصفح طلب غيره |

### الموثوقية

| الحالة | ما يحدث |
|---|---|
| طابور مستقل | `realtime.order-events` منفصل عن طوابير البريد، فلكلٍّ نسخته، وإيقاف عامل البريد لا يؤثر على التحديثات اللحظية |
| تعطّل الـ API أثناء المعالجة | الإقرار بعد الإرسال، فيعيد RabbitMQ تسليم الرسالة |
| رسالة تالفة | تُرفض دون إعادة، فلا توقف الطابور |
| تكرار الرسالة أو وصولها بغير ترتيبها | تطبّق الواجهة الحالة فقط إذا كان `changedAt` أحدث، وتتجاهل الطلب الجديد إذا كان موجوداً |
| انقطاع الاتصال | تعيد الواجهة الاتصال تلقائياً، ثم تعيد تحميل البيانات مرة واحدة لتلتقط ما فاتها |
| RabbitMQ متوقف عند تشغيل الـ API | يعيد المستهلك المحاولة كل 5 ثوانٍ |

### التشغيل

نفس خطوات القسم السابق، مع تثبيت الحزمة الجديدة في الواجهة مرة واحدة:

```bash
cd frontend/ecommerce-store
npm install
npm run dev      # يمرّر /hubs إلى الـ API مع دعم WebSocket
```

### سيناريو العرض (المناقشة)

افتح المتجر في متصفحين منفصلين (مثلاً نافذة عادية ونافذة خاصة): الأول بحساب زبون، والثاني بحساب مسؤول.

**1. الطلب الجديد يظهر فوراً في لوحة المسؤول:** المسؤول على صفحة **All orders**، والزبون ينشئ طلباً، فيظهر الطلب في أعلى القائمة فوراً دون تحديث الصفحة.

**2. موافقة المسؤول تظهر فوراً في صفحة الزبون:** الزبون على صفحة طلبه، والمسؤول يفتح الطلب نفسه ويختار `Approved` ثم **Save**، فتتغير الحالة وشريط التقدّم عند الزبون فوراً دون تحديث الصفحة.

**3. الصلاحيات:** التوكن من أدوات المطوّر ← Application ← Local Storage ← `store.token`.

```bash
curl -i -X POST "http://localhost:5169/hubs/admin-orders/negotiate?negotiateVersion=1"                              # 401: بدون توكن
curl -i -X POST "http://localhost:5169/hubs/admin-orders/negotiate?negotiateVersion=1&access_token=<توكن زبون>"     # 403: ليس مسؤولاً
curl -i -X POST "http://localhost:5169/hubs/orders/negotiate?negotiateVersion=1&access_token=<توكن مسؤول>"          # 403: قناة الزبائن فقط
```

**4. داخل RabbitMQ:** في لوحة RabbitMQ يظهر الطابور `realtime.order-events`، واتصال باسم `api-realtime`.

## طريقة العمل

- الفرع `main` محمي، ولا يُكتب عليه مباشرة.
- كل تعديل يكون في فرع مستقل (مثلاً `feature/auth`)، ثم يُفتح Pull Request يراجعه زميل قبل الدمج.
- لكل عضو Pull Request واحد على الأقل في كل متطلب.

## المتطلب الخامس: التوسّع وتوزيع الأحمال (Scalability & Load Distribution)

يعمل النظام في هذا المتطلب عبر 3 نسخ متطابقة من الـ API (`api-1`، `api-2`، `api-3`) خلف موزّع الحمل Nginx، و3 عمّال بريد (`worker-1`، `worker-2`، `worker-3`) يتقاسمون المهام من نفس طابور RabbitMQ.

### 1. التشغيل وتطبيق التهجير (Migrations)

1. على قاعدة بيانات جديدة، يجب تطبيق تهجيرات EF Core الأربعة (بما فيها جدول `email."ProcessedMessages"` الخاص بعامل البريد):

```bash
dotnet ef database update --project src/Modules/Users --startup-project src/Host --context UsersDbContext
dotnet ef database update --project src/Modules/Products --startup-project src/Host --context ProductsDbContext
dotnet ef database update --project src/Modules/Orders --startup-project src/Host --context OrdersDbContext
dotnet ef database update --project src/Workers/EmailWorker --context EmailWorkerDbContext
```

في حال وجود PostgreSQL محلي على المنفذ `5432`، يمكن توليد سكربت SQL وتنفيذه مباشرة داخل حاوية Docker:

```bash
dotnet ef migrations script --idempotent --project src/Workers/EmailWorker --context EmailWorkerDbContext -o email.sql
docker exec -i distributed-ecommerce-platform-postgres-1 psql -U ecommerce_user -d ecommerce_db < email.sql
```

2. تشغيل جميع الحاويات (14 حاوية) عبر Docker Compose:

```bash
docker compose up -d --build
docker compose ps
```

> عند التشغيل عبر Docker Compose لا تشغّل `dotnet run` ولا `node inventory-service/server.js` محلياً، لأن المنفذين `5169` و`50051` مستخدمان من Nginx وحاوية المخزون.

### 2. أوامر التحكم بالنسخ والعمّال

```bash
docker compose stop api-2            # إيقاف نسخة API بشكل طبيعي
docker compose start api-2           # إعادة تشغيل نسخة API
docker compose kill api-3            # إيقاف مفاجئ لمحاكاة انهيار نسخة
docker compose stop worker-3         # إيقاف عامل بريد
docker compose start worker-3        # إعادة تشغيل عامل بريد
docker compose logs --tail 20 api-1  # عرض سجلات نسخة محددة
docker compose logs worker-1 worker-2 worker-3  # عرض سجلات العمّال الثلاثة
```

### 3. تبرير استراتيجية توزيع الأحمال

- **طلبات الـ API (`/`): `least_conn`** — الطلبات بلا حالة (Stateless) بفضل توكن JWT الموقّع بنفس المفتاح في جميع النسخ والبيانات المشتركة في PostgreSQL وRedis. كلفة الطلبات غير متساوية (قراءة منتج من الكاش تستغرق أجزاء من الميلي ثانية، بينما إنشاء طلب يتضمن اتصال gRPC بالمخزون ومعاملة قاعدة بيانات وOutbox)، لذلك يوجّه `least_conn` كل طلب جديد للنسخة الأقل انشغالاً بدلاً من التوزيع الأعمى (Round Robin).
- **قنوات التحديث اللحظي (`/hubs/`): `ip_hash`** — يبدأ اتصال SignalR بطلب `negotiate` ثم يرتقي إلى WebSocket ويجب أن يصلا لنفس النسخة، لذلك يضمن `ip_hash` الالتصاق (Sticky Sessions) بينما يتولى Redis Backplane إيصال الأحداث بين جميع النسخ.
- **الاستمرارية (Failover):** يراقب Nginx النسخ بشكل سلبي (`max_fails=2 fail_timeout=10s`) مع مهلة اتصال قصيرة (`proxy_connect_timeout 2s`) وإعادة توجيه تلقائية عبر `proxy_next_upstream` للطلبات الآمنة (دون `non_idempotent` لتجنب تكرار طلبات POST).

### 4. المشاكل الخفية عند الانتقال من نسخة واحدة إلى ثلاث (قبل وبعد الإصلاح)

| المشكلة (الحالة المحلية) | ماذا تعطل مع 3 نسخ | الإصلاح المطبّق | النتيجة قبل الإصلاح | النتيجة بعد الإصلاح |
|---|---|---|---|---|
| عدّاد محاولات الدخول في الذاكرة المحلية | سمحت كل نسخة بـ 5 محاولات (15 بدل 5) | `RedisAuthRateLimiterMiddleware` عبر سكربت Lua ذري في Redis | 15 محاولة عبر 3 نسخ | 5 محاولات `401` ثم `429` من أي نسخة |
| إخفاء عنوان العميل وتزوير `X-Forwarded-For` | أظهر Nginx عنوانه، وتجاوز مهاجم الحد بترويسة مزيّفة | `UseForwardedHeaders` مع `ForwardLimit = 1` وكتابة `$remote_addr` في Nginx | 8 من 8 محاولات مسموحة مع ترويسة مزيّفة | 5 محاولات `401` ثم 3 ردود `429` |
| اتصالات SignalR محفوظة في ذاكرة النسخة | استهلكت نسخة واحدة الحدث وفقدت النسختان الأخريان التحديث | إضافة Redis Backplane (`AddStackExchangeRedis`) و`ip_hash` على `/hubs/` | التحديث يصل لنسخة واحدة فقط | وصول `6/6` تحديثات عبر جميع النسخ |
| تخزين صور المنتجات داخل القرص المحلي للحاوية | ملف مرفوع على `api-1` يعيد `404` من `api-2` و`api-3` | ربط حجم Docker مشترك `uploads-data` على `/app/wwwroot/images` | `404` من النسخ الأخرى | `200` من جميع النسخ (`3/3/3`) |
| سباق العمّال الثلاثة على نفس الرسالة (`OrderCreatedConsumer`) | فحص عاملان `ProcessedMessages` معاً قبل الإدراج فأُرسل البريد مرتين | حجز ذري داخل معاملة عبر `INSERT ... ON CONFLICT ("MessageId") DO NOTHING` | 6 من 10 رسائل أُرسلت مرتين | 0 من 10 مكررة (بريد واحد لكل رسالة) |
| انتظار Nginx 60 ثانية عند فحص نسخة متوقفة | علق طلب واحد حتى قُطع الاختبار واستغرق 56 ثانية | إضافة `proxy_connect_timeout 2s;` في `nginx.conf` | 56 ثانية مع 1 طلب مقطوع | 26.5 ثانية، `0` مقطوعة، `0.00%` أخطاء (`max=2.01s`) |
| حفظ Nginx لعناوين IP النسخ عند البدء فقط | إعادة إنشاء `api-2` بعنوان IP جديد أخرجته من التوزيع | إضافة `resolver 127.0.0.11` و`zone` و`resolve` في `nginx.conf` | `api-2` استقبل `0` طلبات | عاد تلقائياً بالتوزيع `10 / 10 / 10` |
| عنوان خدمة المخزون `127.0.0.1` ثابتاً في الكود | داخل الحاوية يشير للحاوية نفسها لا لخدمة المخزون | تمرير `GrpcServices__InventoryUrl=http://inventory-service:50051` | فشل اتصال gRPC | نجاح إنشاء الطلبات (`201`) |
| تعدد ناشري الـ Outbox عبر 3 نسخ API | احتمال نشر نفس الصف مرتين لولا القفل | القفل الموجود مسبقاً `FOR UPDATE SKIP LOCKED` في `OutboxPublisher` | — | عدم تكرار النشر، وOutbox فارغ بعد الإرسال |

### 5. اختبار الحمل (500 طلب) وإيقاف نسخة أثناء التشغيل

سكربت اختبار الحمل موجود في `tests/load/load.js` ويُنفَّذ عبر `grafana/k6` بمعدل 20 طلباً في الثانية لمدة 25 ثانية (500 طلب):

```bash
docker run --rm --network distributed-ecommerce-platform_default -v "$(pwd)/tests/load:/scripts" grafana/k6 run /scripts/load.js
```

أثناء تشغيل الاختبار (بعد ~8 ثوانٍ) أوقف إحدى النسخ:

```bash
docker compose stop api-2
```

**النتائج المُثبتة:**
- **مع `docker stop api-2`:** 501 طلب مكتمل، `0.00%` أخطاء، `0` طلبات مقطوعة، التوزيع `api-1: 225` / `api-2: 50` / `api-3: 226` خلال 26.5 ثانية.
- **مع `docker kill api-3`:** 501 طلب مكتمل، `0.00%` أخطاء، `0` طلبات مقطوعة، التوزيع `api-1: 226` / `api-2: 225` / `api-3: 50` خلال 26.6 ثانية.
- **العمّال الثلاثة:** 15 طلباً متتابعاً وُزِّعت `5 / 5 / 5`، و30 طلباً متزامناً أنتجت 30 بريداً (`11 / 10 / 9`) مع `0` مفقود و`0` مكرر.