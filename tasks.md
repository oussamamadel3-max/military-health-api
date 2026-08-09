# Tasks Checklist

- [x] **Task 1: Result & Generic Result Refinement**
  - **Details**: دراسة ملف الـResult & Generic Result وحسم الهيكلية الجديدة للمشروع.

- [x] **Task 2: Rate Limiting & Infrastructure Study** *(For Understanding & Study)*
  - **Details**: API: دراسة وفهم ملف الـRate Limiting ومكونات مجلد Infrastructure (دراسة وتحليل فقط، بدون تطبيق كود).

- [x] **Task 3: Application/Service Layer DB Context Audit**
  - **Details**: Check the application/service layer & check that no file is directly using `DbContext`.

- [x] **Task 4: Security Checks (CSRF, JWT & XSS)** *(For Understanding & Audit)*
  - **Details**: دراسة وفهم آليات الأمان المطبقة في النظام (CSRF, JWT & XSS) (دراسة وتحليل فني فقط، بدون تطبيق كود).

- [x] **Task 5: Modularize Mapping Profiles**
  - **Details**: Split `mapProfile.cs` into separate profile files (e.g., `RoleProfile.cs`, `documentsProfile.cs`).

- [x] **Task 6: Manual Mapping Audit**
  - **Details**: Check that there is no manual mapping (converting manually without using `mapProfile.cs`).

- [x] **Task 7: Split Entites Data Annotations into separate files**
  - **Details**: Split entity data annotations into separate files.
