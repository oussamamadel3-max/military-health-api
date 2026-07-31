# Tasks Checklist

- [ ] **Task 1: Result & Generic Result Refinement**
  - **Details**: تطوير ملف الـResult & Generic Result ودراسته وتطويره

- [ ] **Task 2: Rate Limiting & Infrastructure Study**
  - **Details**: API: دراسة ملف الـRate Limiting & Infrastructure Folder

- [ ] **Task 3: Generic Filters Across Entities**
  - **Details**: Create generic filters across all entities (e.g., filter across all DB entities with a specific column, such as filtering using "Status" on Documents + Attachments in one place).

- [ ] **Task 4: Application/Service Layer DB Context Audit**
  - **Details**: Check the application/service layer & check that no file is directly using `DbContext`.

- [ ] **Task 5: Security Checks (CSRF, JWT & XSS)**
  - **Details**: Check CSRF & JWT security + XSS injection prevention.

- [ ] **Task 6: Modularize Mapping Profiles**
  - **Details**: Split `mapProfile.cs` into separate profile files (e.g., `RoleProfile.cs`, `documentsProfile.cs`).

- [ ] **Task 7: Manual Mapping Audit**
  - **Details**: Check that there is no manual mapping (converting manually without using `mapProfile.cs`).
