# PROJECT_ROADMAP.md
**Project:** RenoTrack — Renovation Company Website & Project-Tracking Dashboard
**Owner:** Tech Lead / Senior Backend Engineer: Claude · Junior Backend Developer: You
**المصدر الرسمي الوحيد:** SRS.md, Architecture.md, ERD.md, Sequence Diagram.md, StateMachine.md, BusinessRules.md, PermissionMatrix.md, Wireframes.md

> هذا الملف هو خطة التنفيذ الكاملة للمشروع، مقسّمة إلى Phases بنفس الطريقة التي تُبنى بها المشاريع داخل شركات Software حقيقية: كل Phase تنتج شيئًا **يعمل فعليًا (working increment)**، قابل للاختبار، وله Branch و Pull Request خاصين به. لا ننتقل لمرحلة إلا بعد إغلاق التي قبلها.

---

## فلسفة التقسيم (لماذا هذا الترتيب تحديدًا)

نبني الطبقات الداخلية (Domain → Application) **قبل** أي شيء يلمس قاعدة بيانات أو HTTP أو واجهة مستخدم. هذا ليس تعسفًا أكاديميًا — إنه القرار المعماري المذكور صراحة في Architecture.md §3 (Clean Architecture, Dependency Rule): الـ Domain لا يعتمد على أي شيء، والـ Application يعتمد فقط على الـ Domain. لو بدأنا بقاعدة البيانات أو الـ API، سنكتب منطق حساب الـ Angebot (BR-6, Architecture §6.1) متشابكًا مع EF Core أو Controllers، وأي تعديل لاحق في قاعدة البيانات سيكسر منطق العمل. بناء المنطق أولًا، واختباره بوحدات (unit tests) بدون أي DB حقيقية، هو ما يجعل هذا المنطق **موثوقًا وقابلًا لإعادة الاستخدام** لاحقًا في كل من الـ Angebot والـ Invoice (كما يذكر Architecture §6.1 صراحة: "هذا نفس الحساب يُعاد استخدامه للـ Invoices").

بعد ذلك: قاعدة البيانات (Infrastructure) → الـ API → الـ Auth → كل مجموعة Endpoints بترتيب الـ Workflow الفعلي في SRS §5 (Lead → Inspection → Angebot → Review → Send → Project → Invoice) → البريد الإلكتروني → الواجهات (Dashboard ثم Website) → PDF → التلميع النهائي.

---

## أسئلة مفتوحة يجب حسمها قبل أو أثناء مراحل معينة

هذه ليست تخمينات — هي أسئلة صريحة موجودة في SRS.md §10 و PermissionMatrix.md، لن أخمّن إجاباتها:

| # | السؤال | يؤثر على | القرار الافتراضي المقترح (لن أنفذه إلا بموافقتك) |
|---|---|---|---|
| OQ-1 | هل يدير الـ Admin حسابات الـ Inspectors من الداشبورد؟ | Phase 3 (Identity), Phase 10 (Dashboard) | PermissionMatrix.md يفترض **نعم** — سآخذ به ما لم تخبرني غير ذلك |
| OQ-2 | هل الموقع بالألمانية فقط أم ألمانية + إنجليزية؟ | Phase 13 (Website) | SRS يفترض ألمانية فقط لـ v1 — سآخذ به |
| OQ-3 | ما مزود إرسال البريد الإلكتروني؟ (SMTP شركة قائم، أو مزود مثل SendGrid/Postmark) | Phase 9 (Email) | **يجب أن تحدد هذا قبل بدء Phase 9** — سأسألك حينها تحديدًا |
| OQ-4 | هل رفض الـ Angebot طريق مسدود نهائي، أم لاحقًا يدعم "تعديل وإعادة إرسال"؟ | Phase 1 (StateMachine)، لكن غير محظور الآن | StateMachine.md لا يبني هذا الآن عمدًا — سنلتزم بذلك (Lead/Angebot المرفوض = Terminal state) |

بالإضافة، سؤال بيئة عمل (تقني بحت وليس من الوثائق): تحققتُ من جهازك ووجدت: **.NET SDK 10.0.302**, **Node v20.20.2**, **Git 2.54**, أدوات `sqlcmd` موجودة (لكن لا يوجد Docker مثبَّت). سنستخدم SQL Server LocalDB أو Developer Edition محليًا ما لم تفضّل Docker (يمكن تثبيته لاحقًا وقت الحاجة في Phase 2).

---

## خريطة الـ Phases

### Phase 0 — Solution Bootstrap
**الهدف:** إنشاء هيكل الحل (Clean Architecture skeleton) فارغًا، Git repository، وأول Migration فارغة، بدون أي منطق عمل بعد.
**لماذا قبل أي شيء آخر:** كل الأكواد اللاحقة تحتاج مكانًا تعيش فيه بالبنية الصحيحة منذ اليوم الأول (Architecture §3). تأسيس البنية بعد كتابة كود فعلي أصعب بكثير من تأسيسها فارغة.
**ما الذي سنبنيه:**
- `RenoTrack.sln` + 6 مشاريع فارغة: `Domain`, `Application`, `Infrastructure`, `Api`, `Dashboard`, `Website` + مشروعا اختبارات.
- `.gitignore`, `README.md` أولي، `git init` + أول Commit.
- إعداد أساسي لـ solution-level `Directory.Build.props` (نسخة C#، nullable enabled، إلخ).
**ما الذي سيعمل بعد انتهائها:** الحل يُبنى (`dotnet build`) بدون أخطاء؛ لا وظائف بعد — فقط هيكل صحيح.
**Git Branch:** `chore/phase-0-solution-bootstrap`
**Pull Request:** `Phase 0: Solution bootstrap — Clean Architecture skeleton`
**المتطلبات السابقة:** لا شيء (نقطة البداية).
**تبني عليه المراحل القادمة:** كل مرحلة لاحقة تضيف كودًا داخل هذا الهيكل.

---

### Phase 1 — Domain Core: Lead, Inspection, Angebot (+ حساب الإجماليات)
**الهدف:** بناء الـ Domain Entities الأساسية (Lead, Inspection, InspectionPhoto, Angebot, AngebotSection, AngebotItem) ومنطق حساب الإجماليات (LineTotal → Subtotal → NetTotal → VAT breakdown → GrossTotal) كما في Architecture §6.1 و BR-6.
**لماذا هذا أولًا تحديدًا:** هذا هو **قلب المنتج** — السبب الفعلي الذي من أجله يُبنى المشروع (SRS §1.1: توفير 3-4 ساعات يدوية). هذا المنطق يجب أن يكون صحيحًا 100% ومُختبرًا وحدويًا (unit-tested) قبل أن تلمسه أي طبقة أخرى، لأن أي خطأ حسابي هنا سينتقل لاحقًا إلى مستندات قانونية (Angebot/Invoice، BR-5).
**ما الذي سنبنيه:**
- Entities + enums (`LeadStatus`, `AngebotStatus`, `Unit`, `VatRate` كـ enum مقيّد بـ 0/7/16/19 حسب BR-6 وملاحظة Architecture §11).
- الـ Aggregate Root: `Angebot` هو الوحيد الذي يُعدَّل من خلاله `AngebotSection`/`AngebotItem` (Architecture §6).
- منطق `RecalculateTotals()` بالضبط كما في Architecture §6.1 (5 خطوات).
- الحارس (Guard): One Angebot per Lead in non-terminal state (StateMachine §2.4).
- اختبارات وحدة (xUnit) تغطي: عناصر بأسعار متعددة VAT (0/16/19 كما في المستند الحقيقي)، إعادة الحساب عند إضافة/حذف عنصر.
**ما الذي سيكون Working بعد انتهائها:** يمكن، من كود C# فقط (بدون DB ولا API)، إنشاء Angebot، إضافة أقسام وعناصر، ورؤية الإجماليات الصحيحة تُحسب تلقائيًا — ومثبتة باختبارات آلية خضراء.
**Git Branch:** `feature/phase-1-domain-core`
**Pull Request:** `Phase 1: Domain core — Lead/Inspection/Angebot aggregate with VAT-aware totals calculation`
**المتطلبات السابقة:** Phase 0.
**تبني عليه المراحل القادمة:** Phase 1b (Catalog)، Phase 2 (Application يستدعي هذا المنطق)، Phase 8 (Invoice يعيد استخدام نفس الحساب حرفيًا).

---

### Phase 1b — Domain: CatalogItem + منطق "من الكتالوج" و"حفظ كعنصر كتالوج"
**الهدف:** إضافة `CatalogItem` كـ Aggregate مستقل، ومنطق النسخ (لا الربط الحي) بين Catalog وAngebotItem، تنفيذًا حرفيًا لـ BR-8.
**لماذا تأتي بعد الـ Angebot مباشرة وليس معه:** لأنها ميزة منفصلة منطقيًا (Aggregate مستقل تمامًا حسب Architecture §6) لكنها **الأهم تجاريًا** حسب SRS §3.4 ("Analysis of the sample Angebot shows... this is the feature most directly responsible for actually cutting the 3–4 hour manual fill-in time"). فصلها في مرحلة صغيرة خاصة بها يجعلها قابلة للمراجعة والاختبار بمعزل عن تعقيد الـ Angebot نفسه.
**ما الذي سنبنيه:**
- `CatalogItem` entity.
- قاعدة BR-8 كـ domain rule صريحة: أي `AngebotItem` ينسخ القيم وقت الإنشاء، ولا يقرأ من `CatalogItem` لاحقًا أبدًا.
- اختبار وحدة يثبت: تعديل `CatalogItem` لا يغيّر `AngebotItem` قديم تم إنشاؤه منه (هذا الاختبار تحديدًا هو أهم اختبار في هذه المرحلة).
**ما الذي سيكون Working بعد انتهائها:** إنشاء عنصر Angebot من قالب Catalog، مع إثبات آلي أن العزل التاريخي (BR-8) يعمل.
**Git Branch:** `feature/phase-1b-catalog-domain`
**Pull Request:** `Phase 1b: CatalogItem domain — copy-on-create semantics (BR-8)`
**المتطلبات السابقة:** Phase 1.
**تبني عليه المراحل القادمة:** Phase 5 (Angebot builder endpoints تستخدم الكتالوج)، Phase 6 (Catalog management API).

---

### Phase 2 — Application Layer: Commands/Queries (CQRS-lite) لكل من Lead/Inspection/Angebot
**الهدف:** بناء طبقة الـ Use Cases فوق الـ Domain: Command/Query classes + Handlers + DTOs + Validators (FluentValidation)، لكل تدفقات Sequence Diagram §1-5.
**لماذا قبل الـ Infrastructure والـ API:** Architecture §5.1 يوضح أن هذا النمط (CQRS-lite) "keeps controllers thin" — لكن الأهم: نستطيع اختبار كل Handler بمستودعات وهمية (in-memory fakes لـ `IRepository`) بدون قاعدة بيانات حقيقية، فنكتشف أخطاء منطق العمل (الحراسات/Guards من StateMachine.md) بسرعة وبدون بطء اختبارات الـ DB.
**ما الذي سنبنيه:**
- Interfaces: `ILeadRepository`, `IInspectionRepository`, `IAngebotRepository`, `IUnitOfWork`, `IAuditService` (تعريف فقط، بدون تنفيذ — التنفيذ في Phase 3).
- Commands: `CreateLeadCommand`, `ScheduleInspectionCommand`, `CompleteInspectionCommand`, `CreateAngebotCommand`, `AddAngebotSectionCommand`, `AddAngebotItemCommand`, `SubmitAngebotForReviewCommand`, `RequestAngebotChangesCommand`, `ApproveAngebotCommand`.
- كل الحراسات المذكورة في StateMachine.md §1 و§2 (مثال: `CreateAngebot` فقط إذا `Lead.Status == InspectionDone` و"لا يوجد Angebot مفتوح آخر").
- اختبارات Application (باستخدام fakes) تغطي المسارات الناجحة والحراسات الفاشلة.
**ما الذي سيكون Working بعد انتهائها:** كل تدفقات العمل الداخلية (بدون الإرسال للعميل بعد) قابلة للتنفيذ عبر Handlers مختبرة، جاهزة لتُستدعى من أي واجهة (API أو حتى CLI اختباري).
**Git Branch:** `feature/phase-2-application-layer`
**Pull Request:** `Phase 2: Application layer — Lead/Inspection/Angebot commands, queries, and guards`
**المتطلبات السابقة:** Phase 1, Phase 1b.
**تبني عليه المراحل القادمة:** Phase 4 و5 (الـ API Controllers ستكون رقيقة جدًا — فقط تستدعي هذه الـ Handlers).

---

### Phase 3 — Infrastructure: EF Core, Repositories, DB Schema, Identity
**الهدف:** تنفيذ الـ Interfaces المعرَّفة في Phase 2 فعليًا فوق SQL Server + EF Core، وإعداد ASP.NET Core Identity للمستخدمين الداخليين.
**لماذا الآن:** لأن الـ Interfaces والعقود (contracts) أصبحت واضحة ومستقرة من Phase 2 — تنفيذها الآن يعني أننا نصمم Schema قاعدة البيانات وهي مبنية على احتياج منطق عمل فعلي ومُختبر، لا تخمين مسبق (هذا بالضبط سبب وجود ERD.md كملف تفصيلي منفصل ينفّذه هذا الـ Phase حرفيًا).
**ما الذي سنبنيه:**
- `RenoTrackDbContext` + EF Core configurations (Fluent API) مطابقة تمامًا لـ ERD.md §2 (Physical Schema Notes: القيود، الفهارس من §3، أنواع البيانات `decimal(18,2)` إلخ من Architecture §11).
- تنفيذ الـ Repositories.
- أول Migration حقيقية (`InitialCreate`).
- ASP.NET Core Identity (`User` مع `Role` claim: Admin/Inspector) — يحل OQ-1 بافتراض "نعم، Admin يدير الحسابات".
- `NumberSequence` + `INumberGeneratorService` (Architecture §8) — بزيادة أتومية داخل نفس الـ transaction.
**ما الذي سيكون Working بعد انتهائها:** قاعدة بيانات حقيقية تعمل محليًا؛ يمكن تشغيل اختبارات تكامل (integration tests) تُنشئ Lead → Inspection → Angebot فعليًا وتُقرأ من SQL Server.
**Git Branch:** `feature/phase-3-infrastructure-efcore`
**Pull Request:** `Phase 3: Infrastructure — EF Core schema, repositories, Identity, number sequences`
**المتطلبات السابقة:** Phase 2.
**تبني عليه المراحل القادمة:** كل الـ API Phases التالية (4-9) تعتمد على DB حقيقية من هنا.

---

### Phase 4 — API: Authentication + Lead & Inspection Endpoints
**الهدف:** أول API فعلي قابل للاستدعاء عبر HTTP: تسجيل الدخول (JWT) + Endpoints الخاصة بـ Leads والـ Inspections.
**لماذا الآن:** هذا أول جزء "يعمل من طرف لطرف" (end-to-end) — من طلب HTTP حتى قاعدة بيانات فعلية. البدء بالـ Auth تحديدًا لأن كل Endpoint لاحق محمي بأدوار (Admin/Inspector)، وفقًا لـ PermissionMatrix.md.
**ما الذي سنبنيه:**
- `AuthController`: `POST /api/v1/auth/login` (Sequence Diagram §11) — JWT + Refresh Token.
- `LeadsController`: `POST /api/v1/leads` (عام — بلا Auth، للموقع)، و`GET`/`PATCH` محمية بـ `[Authorize]` مع تقييد نطاق الـ Inspector (`AssignedInspectorId == self`) حسب PermissionMatrix §1.
- `InspectionsController`: جدولة (Admin)، رفع صور، إكمال (Inspector فقط) — PermissionMatrix §2.
- Middleware للأخطاء بصيغة RFC 7807 ProblemDetails (Architecture §5.3) — يُبنى هنا لأن كل Controller من الآن فصاعدًا سيعتمد عليه.
- `IFileStorage` (تنفيذ `LocalDiskFileStorage`) لصور الـ Inspection (Architecture §9).
**ما الذي سيكون Working بعد انتهائها:** تسجيل دخول حقيقي، إنشاء Lead من نموذج عام، جدولة معاينة، رفع صور وإكمال معاينة — كله عبر Postman/HTTP فعليًا، بصلاحيات مطبَّقة بشكل صحيح.
**Git Branch:** `feature/phase-4-api-auth-leads-inspections`
**Pull Request:** `Phase 4: API — JWT auth, Lead & Inspection endpoints with role-scoped access`
**المتطلبات السابقة:** Phase 3.
**تبني عليه المراحل القادمة:** كل الـ Endpoints اللاحقة تستخدم نفس نمط الـ Auth/Authorization.

---

### Phase 5 — API: Angebot Builder + Internal Review Workflow
**الهدف:** Endpoints بناء الـ Angebot (أقسام، عناصر، من الكتالوج أو مخصص) + حلقة المراجعة الداخلية الكاملة (Submit → Approve/Request Changes).
**لماذا بعد Lead/Inspection مباشرة:** لأنها الخطوة التالية في تسلسل الـ Workflow الفعلي (SRS §5 end-to-end flow)، ولأن Phase 1+1b+2 جهّزا كل المنطق الثقيل مسبقًا — هذه المرحلة عمليًا "توصيل الأسلاك" (wiring) بين API نظيف ومنطق مُختبر بالفعل.
**ما الذي سنبنيه:**
- `AngeboteController` بكل مسارات Architecture §5.2: إنشاء مسودة، أقسام، عناصر (من كتالوج أو مخصص)، "حفظ كعنصر كتالوج"، تكرار Angebot سابق (FR-4.11)، submit/approve/request-changes.
- `CatalogItemsController`: بحث/عرض (كلا الدورين)، إنشاء مباشر (Admin فقط) — PermissionMatrix §6.
- تطبيق قيد "القفل بعد InReview" (StateMachine §2.4 Invariant) على مستوى الـ Command Handler.
**ما الذي سيكون Working بعد انتهائها:** دورة كاملة: Inspector يبني عرض سعر بأقسام وعناصر (وبعضها من الكتالوج)، يرسله للمراجعة، Admin يوافق أو يطلب تعديلات، والحلقة تتكرر — كل هذا حقيقي عبر HTTP، بالإجماليات الصحيحة.
**Git Branch:** `feature/phase-5-angebot-builder-review`
**Pull Request:** `Phase 5: API — Angebot builder (sections/items/catalog) + internal review loop`
**المتطلبات السابقة:** Phase 4، Phase 1b.
**تبني عليه المراحل القادمة:** Phase 7 (الإرسال يبدأ من حالة `ApprovedInternally` الناتجة هنا).

---

### Phase 6 — API: Token-Link Mechanism + Public Angebot Decision Endpoints
**الهدف:** آلية الروابط المؤقتة الآمنة (بلا تسجيل دخول) وربطها بإرسال ومعاينة/قرار العميل على الـ Angebot.
**لماذا مرحلة منفصلة بذاتها:** لأنها **حدود أمنية حرجة** (Architecture §7.2, §12): أول سطح API غير محمي بـ JWT بالكامل، ومصمَّم عمدًا كنظام "مصادقة صغير موازٍ" بسيط. عزلها في Phase خاصة يسمح بمراجعة أمنية مركّزة (rate limiting، عشوائية التوكن، صلاحية/انتهاء) دون تشتيتها وسط منطق عمل آخر.
**ما الذي سنبنيه:**
- `ITokenLinkService` + `TokenLinkRepository`.
- `SendAngebotCommand` (توليد توكن 32-byte عشوائي، `POST /api/v1/angebote/{id}/send`).
- `PublicController`: `GET /api/v1/public/angebote/{token}` و`POST .../decision` — مع كل الفحوصات في Sequence Diagram §12 (NotFound / نوع خاطئ / منتهي / مستخدم من قبل — BR-4).
- Rate limiting أساسي على مسارات `/api/v1/public/*` (Architecture §12).
- ملاحظة: إرسال البريد الفعلي **placeholder فقط** في هذه المرحلة (تنفيذ حقيقي في Phase 9) — نستخدم `IEmailSender` بتنفيذ وهمي/console-logger مؤقت حتى لا نُعطّل هذه المرحلة بانتظار قرار OQ-3.
**ما الذي سيكون Working بعد انتهائها:** عرض سعر مُعتمد داخليًا يُنتج رابطًا حقيقيًا صالحًا لمرة واحدة، يعرض العرض بشكل read-only، ويسمح للعميل (بدون حساب) بالموافقة أو الرفض — مع منع إعادة الاستخدام فعليًا (مُختبر).
**Git Branch:** `feature/phase-6-token-links-public-angebot`
**Pull Request:** `Phase 6: Token-link mechanism + public Angebot view/decision endpoints (BR-4)`
**المتطلبات السابقة:** Phase 5.
**تبني عليه المراحل القادمة:** Phase 7 (تحويل Angebot المعتمد لمشروع)، Phase 8 (نفس آلية التوكن تُعاد للفواتير).

---

### Phase 7 — API: Convert Angebot → Project
**الهدف:** تحويل عرض السعر الموافَق عليه من العميل إلى مشروع فعلي.
**لماذا مرحلة صغيرة منفصلة:** رغم صغرها، فهي **نقطة تحوّل قانونية/تجارية** (BR-2: "A Project represents committed, paid work"). فصلها يجعل مراجعتها (Code Review) مركّزة تمامًا على صحة الحارس (`Angebot.Status == CustomerApproved`) وعدم وجود أي مسار يتجاوزه.
**ما الذي سنبنيه:**
- `ProjectsController`: `POST /api/v1/angebote/{id}/convert-to-project`.
- `Customer` entity + إنشاء/ربط تلقائي من بيانات الـ Lead (Sequence Diagram §7).
- `Project` مع `AgreedTotal` = لقطة (snapshot) من `Angebot.GrossTotal` وقت التحويل (ERD.md ملاحظة صريحة: لا يتحرك لاحقًا حتى لو تغيّر الـ Angebot).
**ما الذي سيكون Working بعد انتهائها:** عرض سعر تمت الموافقة عليه من العميل يتحول بضغطة واحدة إلى مشروع نشط، مع عميل تم إنشاؤه/ربطه تلقائيًا.
**Git Branch:** `feature/phase-7-angebot-to-project`
**Pull Request:** `Phase 7: API — Convert approved Angebot into a Project (BR-2)`
**المتطلبات السابقة:** Phase 6.
**تبني عليه المراحل القادمة:** Phase 8 (الفواتير تُبنى فوق Project).

---

### Phase 8 — API: Invoices, Splitting, Payment Tracking, Project Completion
**الهدف:** إنشاء/تقسيم الفواتير على المشروع، إرسالها عبر نفس آلية التوكن، تسجيل الدفع يدويًا، وإغلاق المشروع.
**لماذا الآن:** آخر حلقة في دورة العمل التجارية الكاملة من SRS §5 flowchart. تعتمد مباشرة على Project (Phase 7) وتعيد استخدام نفس منطق حساب الـ VAT من Phase 1 (Architecture §6.1: "This same calculation is reused for Invoices") ونفس آلية التوكن من Phase 6.
**ما الذي سنبنيه:**
- `InvoicesController`: رصيد متبقٍ (`GetRemainingInvoiceBalanceQuery`، BR-3 تحذير لا حظر)، إنشاء، إرسال (PDF placeholder — التوليد الفعلي في Phase 11)، `mark-paid`, `void` (مع سبب، BR-9).
- `Payment` entity، بتصميم متوافق مسبقًا مع بوابة دفع مستقبلية دون تغيير Schema (FR-8.5 — القيد الوحيد الذي طلبه الـ Product Owner صراحة أن نبنيه بمرونة زائدة عمدًا).
- `CompleteProjectCommand` مع الحارس (كل الفواتير Paid أو Void) ومسار الـ Override بسبب إلزامي (FR-8.6).
- Scheduled check بسيط لتحويل الفواتير المتأخرة إلى `Overdue` (StateMachine §3.3).
**ما الذي سيكون Working بعد انتهائها:** دورة العمل التجارية الكاملة تعمل من طرف لطرف عبر API فقط: Lead → Inspection → Angebot → Review → Send → Won → Project → Invoices → Paid → Completed.
**Git Branch:** `feature/phase-8-invoices-payments-project-completion`
**Pull Request:** `Phase 8: API — Invoice splitting, payment tracking, project completion guard`
**المتطلبات السابقة:** Phase 7.
**تبني عليه المراحل القادمة:** Phase 11 (PDF الفعلي للفواتير).

---

### Phase 9 — Email Service Integration (حقيقي، وليس Placeholder)
**الهدف:** استبدال الـ `IEmailSender` الوهمي بتنفيذ حقيقي (SMTP أو مزوّد Transactional)، وكل القوالب الألمانية المطلوبة.
**لماذا بعد اكتمال الـ API بالكامل وليس أثناءه:** لأن كل نقاط الإرسال (Lead جديد، Submit for review، قرار العميل، إرسال Angebot/Invoice) أصبحت معروفة ومستقرة تمامًا من المراحل 4-8 — نبني القوالب مرة واحدة بدل تحديثها في كل Phase سابقة.
**⚠️ يتطلب قرارك في OQ-3 قبل البدء فعليًا في هذه المرحلة.**
**ما الذي سنبنيه:**
- تنفيذ `IEmailSender` (SMTP client أو SDK المزوّد المُختار).
- قوالب بالألمانية (FR-9.3): Angebot token link، Invoice token link، Lead جديد من الموقع، Angebot submitted for review، قرار العميل.
- إعادة محاولة بسيطة عند فشل عابر (Architecture §10 — لا حاجة لـ message broker بهذا الحجم).
**ما الذي سيكون Working بعد انتهائها:** كل الإشعارات المذكورة في FR-9.1/9.2 تصل فعليًا كبريد إلكتروني حقيقي.
**Git Branch:** `feature/phase-9-email-integration`
**Pull Request:** `Phase 9: Real email delivery — SMTP/provider integration + German templates`
**المتطلبات السابقة:** Phase 8. **+ قرارك بشأن OQ-3.**
**تبني عليه المراحل القادمة:** لا شيء تقني، لكنها ضرورية لتجربة End-to-End حقيقية قبل عرض المشروع.

---

### Phase 10 — Dashboard (Angular): Lead Pipeline + Inspection Screens
**الهدف:** أول واجهة مستخدم حقيقية — تسجيل الدخول، Kanban pipeline لللـ Leads (Wireframe B2)، شاشة تفاصيل Lead (C1)، شاشة معاينة Mobile-First (C3).
**لماذا الداشبورد قبل الموقع العام:** الداشبورد هو "التسليم الأساسي" (Architecture §1: "The dashboard is the primary deliverable") — القيمة التجارية الحقيقية للعميل. الموقع العام أبسط تقنيًا (Razor Pages ثابتة) ويمكن بناؤه لاحقًا بسرعة أكبر بمجرد استقرار الـ API.
**ما الذي سنبنيه:**
- إعداد مشروع Angular، هيكل المجلدات، HTTP interceptor لإرفاق JWT.
- شاشة B1 (Login)، B2 (Pipeline بفلاتر Status/Inspector/تاريخ)، C1 (Lead detail + Activity Timeline)، C2 (Modal جدولة معاينة)، C3 (شاشة معاينة Mobile-first مع رفع صور).
- تطبيق قواعد الصلاحيات من PermissionMatrix.md على مستوى الواجهة أيضًا (route guards) — مع العلم أن التطبيق الحقيقي دائمًا من طرف السيرفر (Architecture §7.1).
**ما الذي سيكون Working بعد انتهائها:** Admin وInspector يستطيعان فعليًا تسجيل الدخول، رؤية الـ pipeline، جدولة معاينة، وإكمال معاينة بصور من الموبايل — تجربة مستخدم حقيقية أول مرة.
**Git Branch:** `feature/phase-10-dashboard-pipeline-inspections`
**Pull Request:** `Phase 10: Dashboard — login, Lead pipeline, Inspection screens (mobile-first)`
**المتطلبات السابقة:** Phase 4، Phase 9 (تجربة كاملة تحتاج بريد فعلي، لكن يمكن البدء بالواجهة بالتوازي فنيًا إن رغبت).
**تبني عليه المراحل القادمة:** Phase 11 يعيد استخدام نفس الـ Angular shell/routing/auth.

---

### Phase 11 — Dashboard: Angebot Builder UI + Catalog Picker
**الهدف:** واجهة بناء العرض التفاعلية بالكامل (Wireframe D1، D2) مع الإجماليات الحية.
**لماذا مرحلة منفصلة عن باقي الداشبورد:** هي أعقد شاشة في كامل المشروع (SRS §3.4 — أكبر عدد Functional Requirements لأي ميزة واحدة)، تستحق تركيزًا كاملًا بمعزل عن باقي الشاشات الأبسط.
**ما الذي سنبنيه:**
- محرر D1: أقسام قابلة للطي، إضافة عنصر (من كتالوج أو مخصص)، "حفظ كعنصر كتالوج"، تكرار عرض سابق.
- Modal D2 (بحث الكتالوج).
- إعادة حساب الإجماليات لحظيًا في الواجهة (استدعاء الـ API بعد كل تعديل، كما في Sequence Diagram §4).
**ما الذي سيكون Working بعد انتهائها:** Inspector يبني عرض سعر كامل بواجهة مطابقة تمامًا لتدفق العمل الحقيقي، من الصفر حتى Submit for Review.
**Git Branch:** `feature/phase-11-dashboard-angebot-builder`
**Pull Request:** `Phase 11: Dashboard — Angebot builder UI with live totals + Catalog picker`
**المتطلبات السابقة:** Phase 5، Phase 10.
**تبني عليه المراحل القادمة:** Phase 12 (شاشة المراجعة D3 تعيد استخدام نفس عرض القراءة).

---

### Phase 12 — Dashboard: Review Workflow + Project/Invoice UI
**الهدف:** شاشة مراجعة الـ Admin (D3)، وشاشات المشروع والفواتير (E1-E3).
**لماذا الآن:** تُكمل تجربة الـ Admin الكاملة، معتمدة مباشرة على واجهات وAPI اكتملا في المراحل السابقة (5، 8، 11).
**ما الذي سنبنيه:**
- D3: عرض قراءة فقط + تعليقات مراجعة + Approve/Request Changes.
- E1: تفاصيل المشروع (الرصيد المتبقي، الفواتير، Mark Completed).
- E2/E3: إنشاء فاتورة، تسجيل دفع.
**ما الذي سيكون Working بعد انتهائها:** دورة العمل التجارية بأكملها قابلة للتنفيذ من واجهة المستخدم فقط، بدون أي استدعاء API يدوي.
**Git Branch:** `feature/phase-12-dashboard-review-projects-invoices`
**Pull Request:** `Phase 12: Dashboard — Angebot review, Project detail, Invoice management`
**المتطلبات السابقة:** Phase 11.
**تبني عليه المراحل القادمة:** لا شيء تقني إضافي — هذا آخر جزء من الداشبورد.

---

### Phase 13 — Public Website (Razor Pages): Marketing + Contact Form + Token-Link Customer Pages
**الهدف:** الموقع العام الكامل (A1، A2) وصفحات العميل بالرابط المؤقت (A3، A4).
**لماذا في هذا الترتيب المتأخر:** فنيًا مستقل تمامًا عن الداشبورد (Architecture §2)، ويعتمد فقط على API عام مستقر (Phase 6، 8) — لا فائدة من بنائه قبل استقرار تلك الـ Endpoints.
**ما الذي سنبنيه:**
- A1 (Home)، A2 (نموذج تواصل → `POST /api/v1/leads` العام)، صفحات Impressum/Datenschutzerklärung (FR-1.4، قانوني إلزامي).
- A3 (عرض/قرار Angebot بالتوكن)، A4 (عرض Invoice بالتوكن) — تُقرأان من نفس `/api/v1/public/*` من Phase 6/8.
- SEO أساسي (عناوين، meta descriptions، عناوين دلالية — FR-1.5).
**ما الذي سيكون Working بعد انتهائها:** المشروع بأكمله يعمل من طرف لطرف حقيقي: زائر يملأ نموذج تواصل → يصبح Lead → دورة كاملة → عميل يوافق على عرض سعر من رابط بريده الحقيقي.
**Git Branch:** `feature/phase-13-public-website`
**Pull Request:** `Phase 13: Public Website — marketing pages, contact form, token-link customer pages`
**المتطلبات السابقة:** Phase 6، Phase 8، Phase 9.
**تبني عليه المراحل القادمة:** لا شيء تقني.

---

### Phase 14 — PDF Generation (Angebot & Invoice)
**الهدف:** استبدال الـ PDF placeholders من Phase 6/8 بتوليد PDF حقيقي، مطابق قانونيًا لـ BR-5 (حقول الفاتورة الألمانية الإلزامية).
**لماذا بهذا التأخير المتعمد:** PDF تفصيل عرضي (presentation concern) وليس منطق عمل — بناؤه بعد استقرار كل البيانات التي سيعرضها يوفر إعادة عمل كبيرة (لو بُني مبكرًا وتغيّر شكل البيانات لاحقًا).
**ما الذي سنبنيه:**
- `IPdfGenerator` + تنفيذ (مكتبة HTML→PDF لـ .NET، Architecture §4).
- قالب Angebot PDF (مطابق للمستند الورقي الحقيقي المرجعي).
- قالب Invoice PDF بكل الحقول الإلزامية لـ §14 UStG (BR-5): بيانات الشركة + الرقم الضريبي، بيانات العميل، تاريخ الفاتورة، رقم متسلسل فريد، الوصف/الكمية، صافي المبلغ، نسبة/قيمة الضريبة، الإجمالي.
**ما الذي سيكون Working بعد انتهائها:** كل بريد Angebot/Invoice يحمل الآن مرفق PDF حقيقي وقابل للتنزيل من الداشبورد والموقع.
**Git Branch:** `feature/phase-14-pdf-generation`
**Pull Request:** `Phase 14: PDF generation for Angebot & Invoice (BR-5 compliant)`
**المتطلبات السابقة:** Phase 13 (أو بالتوازي مع نهايتها).
**تبني عليه المراحل القادمة:** لا شيء تقني.

---

### Phase 15 — Polish: Audit Log UI, Filtering/Search, Security Hardening, Legal Review
**الهدف:** المرحلة الأخيرة قبل اعتبار v1 "جاهزًا للإطلاق" فعليًا.
**لماذا آخر مرحلة:** كل هذه العناصر تحتاج نظامًا مكتملًا لتُختبر بمعنى حقيقي (Audit Log مثلًا يحتاج بيانات فعلية من كل الـ Phases السابقة لتظهر قيمته).
**ما الذي سنبنيه:**
- شاشة Audit Log (Admin فقط، PermissionMatrix §8) تعرض `AuditLog` table.
- تحسين الفلترة/البحث في الـ pipeline والفواتير.
- مراجعة أمنية شاملة: CORS ضيق (Architecture §12)، Rate limiting على كل المسارات العامة، مراجعة كل `[Authorize]` مقابل PermissionMatrix.md سطرًا بسطر.
- مراجعة الامتثال القانوني (GDPR — إجراء تصدير/حذف بيانات يدوي بمساعدة الـ Admin كما يكفي لـ v1).
**ما الذي سيكون Working بعد انتهائها:** v1 كامل، جاهز للعرض كمنتج حقيقي — وهذه هي نقطة "الإطلاق".
**Git Branch:** `chore/phase-15-polish-hardening`
**Pull Request:** `Phase 15: v1 polish — audit log UI, security hardening, GDPR review`
**المتطلبات السابقة:** كل المراحل من 0 إلى 14.
**تبني عليه المراحل القادمة:** أي Future Enhancement من SRS §9 (خارج نطاق v1).

---

## جدول ملخّص سريع

| # | Phase | Branch | يعتمد على |
|---|---|---|---|
| 0 | Solution Bootstrap | `chore/phase-0-solution-bootstrap` | — |
| 1 | Domain Core (Lead/Inspection/Angebot) | `feature/phase-1-domain-core` | 0 |
| 1b | Domain: CatalogItem | `feature/phase-1b-catalog-domain` | 1 |
| 2 | Application Layer | `feature/phase-2-application-layer` | 1, 1b |
| 3 | Infrastructure (EF Core, Identity) | `feature/phase-3-infrastructure-efcore` | 2 |
| 4 | API: Auth + Lead/Inspection | `feature/phase-4-api-auth-leads-inspections` | 3 |
| 5 | API: Angebot Builder + Review | `feature/phase-5-angebot-builder-review` | 4, 1b |
| 6 | API: Token Links + Public Angebot | `feature/phase-6-token-links-public-angebot` | 5 |
| 7 | API: Angebot → Project | `feature/phase-7-angebot-to-project` | 6 |
| 8 | API: Invoices + Payments | `feature/phase-8-invoices-payments-project-completion` | 7 |
| 9 | Email Integration (real) | `feature/phase-9-email-integration` | 8 + OQ-3 |
| 10 | Dashboard: Pipeline + Inspection | `feature/phase-10-dashboard-pipeline-inspections` | 4, 9 |
| 11 | Dashboard: Angebot Builder UI | `feature/phase-11-dashboard-angebot-builder` | 5, 10 |
| 12 | Dashboard: Review + Project/Invoice UI | `feature/phase-12-dashboard-review-projects-invoices` | 11 |
| 13 | Public Website | `feature/phase-13-public-website` | 6, 8, 9 |
| 14 | PDF Generation | `feature/phase-14-pdf-generation` | 13 |
| 15 | Polish & Hardening | `chore/phase-15-polish-hardening` | 0–14 |

---

## قواعد العمل طوال المشروع (تذكير دائم)

1. لا ننتقل لخطوة تالية داخل أي Phase حتى تخبرني أنك انتهيت من تنفيذ الحالية.
2. كل ملف جديد: اسمه، مساره، وسبب وجوده هناك — قبل إنشائه.
3. كل Package جديدة: لماذا، وكيف تُثبَّت — قبل تثبيتها.
4. كل أمر CLI: شرح قبل التشغيل.
5. كل Commit: رسالة احترافية مقترحة، بسبب واضح.
6. نهاية كل Phase: Branch name + Commit message + PR title + PR description جاهزة، بالإضافة إلى Code Review كامل (ما سيطلبه مراجع حقيقي، وما يمكن تحسينه لاحقًا).
7. أي غموض في الوثائق → أسأل، لا أخمّن.
8. أي قرار هندسي أفضل من الوثائق → أذكره مع الإيجابيات/السلبيات، ثم ألتزم بالوثائق ما لم تطلب التغيير.

---

## الخطوة التالية

هذا الملف يغطي **التخطيط فقط** — لم يُكتب أي كود بعد، كما طلبت.

قبل أن أبدأ فعليًا في **Phase 0**، أحتاج تأكيدك على نقطتين عمليتين بسيطتين:
1. هل تريد `git init` في هذا المجلد الآن (المجلد فارغ حاليًا وليس Git repository بعد)؟
2. هل تفضّل SQL Server LocalDB (الأبسط، يعمل محليًا بدون تثبيت إضافي على Windows) أم Docker container لقاعدة البيانات؟ (هذا القرار فعليًا مطلوب في Phase 3، لكن جيد تحديده مبكرًا).

بمجرد ردك، سأبدأ Phase 0 بالطريقة المتفق عليها: شرح الهدف → شرح "لماذا الآن" → شرح الـ Architecture الخاص بها → كيف يفكر Senior Developer قبل الكتابة → ثم خطوة كود واحدة في كل مرة.
