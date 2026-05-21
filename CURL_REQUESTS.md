# JOIN CRM - Curl Requests

## Variables
```bash
BASE_URL="http://localhost:5267"
AUTH_TOKEN="Bearer YOUR_JWT_TOKEN"
COMPANY_ID="00000000-0000-0000-0000-000000000001"
USER_ID="00000000-0000-0000-0000-000000000002"
CUSTOMER_ID="00000000-0000-0000-0000-000000000003"
PERSON_ID="00000000-0000-0000-0000-000000000004"
TICKET_ID="00000000-0000-0000-0000-000000000005"
PROJECT_ID="00000000-0000-0000-0000-000000000006"
```

## AUTH ENDPOINTS

### Setup Password
```bash
curl -X POST "$BASE_URL/api/v1/auth/setup-password" \
  -H "Content-Type: application/json" \
  -d '{
    "token": "setup-token-from-email",
    "newPassword": "SecurePassword123!",
    "confirmPassword": "SecurePassword123!"
  }'
```

### Forgot Password
```bash
curl -X POST "$BASE_URL/api/v1/auth/forgot-password" \
  -H "Content-Type: application/json" \
  -d '{
    "email": "user@example.com"
  }'
```

### Reset Password
```bash
curl -X POST "$BASE_URL/api/v1/auth/reset-password" \
  -H "Content-Type: application/json" \
  -d '{
    "token": "recovery-token-from-email",
    "newPassword": "NewSecurePassword123!",
    "confirmPassword": "NewSecurePassword123!"
  }'
```

## WORKSPACES ENDPOINTS

### Get My Companies
```bash
curl -X GET "$BASE_URL/api/v1/workspaces/my-companies" \
  -H "Authorization: $AUTH_TOKEN"
```

### Get My Roles by Company
```bash
curl -X GET "$BASE_URL/api/v1/workspaces/$COMPANY_ID/my-roles" \
  -H "Authorization: $AUTH_TOKEN"
```

### Switch Company
```bash
curl -X POST "$BASE_URL/api/v1/workspaces/switch-company" \
  -H "Authorization: $AUTH_TOKEN" \
  -H "Content-Type: application/json" \
  -d "{\"companyId\": \"$COMPANY_ID\"}"
```

## COMPANIES ENDPOINTS

### Get Company by ID
```bash
curl -X GET "$BASE_URL/api/v1/companies/$COMPANY_ID" \
  -H "Authorization: $AUTH_TOKEN"
```

### Get Companies Paged
```bash
curl -X GET "$BASE_URL/api/v1/companies?pageNumber=1&pageSize=10&searchTerm=acme" \
  -H "Authorization: $AUTH_TOKEN"
```

### Create Company
```bash
curl -X POST "$BASE_URL/api/v1/companies" \
  -H "Authorization: $AUTH_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "name": "New Company S.A.",
    "taxId": "123456789",
    "commercialName": "New Company",
    "email": "contact@newcompany.com",
    "phone": "+1234567890",
    "website": "https://newcompany.com",
    "industryId": "industry-id"
  }'
```

### Update Company
```bash
curl -X PUT "$BASE_URL/api/v1/companies/$COMPANY_ID" \
  -H "Authorization: $AUTH_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "name": "Updated Company Name",
    "taxId": "123456789",
    "commercialName": "Updated Company",
    "email": "newemail@company.com",
    "phone": "+0987654321",
    "website": "https://updated.com",
    "industryId": "industry-id"
  }'
```

### Delete Company
```bash
curl -X DELETE "$BASE_URL/api/v1/companies/$COMPANY_ID" \
  -H "Authorization: $AUTH_TOKEN"
```

## CUSTOMERS ENDPOINTS

### Get Customers Paged
```bash
curl -X GET "$BASE_URL/api/v1/customers?pageNumber=1&pageSize=10&customerCode=C001&personName=John&isActive=true" \
  -H "Authorization: $AUTH_TOKEN"
```

### Get Customer by ID
```bash
curl -X GET "$BASE_URL/api/v1/customers/$CUSTOMER_ID" \
  -H "Authorization: $AUTH_TOKEN"
```

### Create Customer
```bash
curl -X POST "$BASE_URL/api/v1/customers" \
  -H "Authorization: $AUTH_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "customerCode": "CUST-001",
    "personId": "'$PERSON_ID'",
    "personLifecycleStage": 1,
    "isActive": true,
    "notes": "VIP customer"
  }'
```

### Update Customer
```bash
curl -X PUT "$BASE_URL/api/v1/customers/$CUSTOMER_ID" \
  -H "Authorization: $AUTH_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "customerCode": "CUST-001-UPD",
    "personLifecycleStage": 2,
    "isActive": true,
    "notes": "Updated VIP customer"
  }'
```

### Delete Customer
```bash
curl -X DELETE "$BASE_URL/api/v1/customers/$CUSTOMER_ID" \
  -H "Authorization: $AUTH_TOKEN"
```

## PERSONS ENDPOINTS

### Get Persons Paged
```bash
curl -X GET "$BASE_URL/api/v1/persons?pageNumber=1&pageSize=10&firstName=John&lastName=Doe" \
  -H "Authorization: $AUTH_TOKEN"
```

### Get Person by ID
```bash
curl -X GET "$BASE_URL/api/v1/persons/$PERSON_ID" \
  -H "Authorization: $AUTH_TOKEN"
```

### Create Person
```bash
curl -X POST "$BASE_URL/api/v1/persons" \
  -H "Authorization: $AUTH_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "firstName": "John",
    "middleName": "Michael",
    "lastName": "Doe",
    "secondLastName": "Smith",
    "commercialName": "J.M. Doe Trading",
    "personType": 1,
    "genderId": "gender-id",
    "identificationTypeId": "id-type-id",
    "identificationNumber": "123456789",
    "birthDate": "1990-01-15",
    "email": "john.doe@example.com",
    "phone": "+1234567890"
  }'
```

### Update Person
```bash
curl -X PUT "$BASE_URL/api/v1/persons/$PERSON_ID" \
  -H "Authorization: $AUTH_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "firstName": "John",
    "middleName": "Michael",
    "lastName": "Doe Updated",
    "secondLastName": "Smith",
    "commercialName": "J.M. Doe Trading Updated",
    "personType": 1,
    "genderId": "gender-id",
    "identificationTypeId": "id-type-id",
    "identificationNumber": "987654321",
    "birthDate": "1990-01-15",
    "email": "john.newemail@example.com",
    "phone": "+0987654321"
  }'
```

### Delete Person
```bash
curl -X DELETE "$BASE_URL/api/v1/persons/$PERSON_ID" \
  -H "Authorization: $AUTH_TOKEN"
```

## TICKETS ENDPOINTS

### Get Tickets Paged
```bash
curl -X GET "$BASE_URL/api/v1/tickets?pageNumber=1&pageSize=10&search=urgent&isVisibleToExternals=false" \
  -H "Authorization: $AUTH_TOKEN"
```

### Get System-Wide Tickets (SuperAdmin)
```bash
curl -X GET "$BASE_URL/api/v1/tickets/system-wide?pageNumber=1&pageSize=10" \
  -H "Authorization: $AUTH_TOKEN"
```

### Get Ticket by ID
```bash
curl -X GET "$BASE_URL/api/v1/tickets/$TICKET_ID" \
  -H "Authorization: $AUTH_TOKEN"
```

### Create Ticket
```bash
curl -X POST "$BASE_URL/api/v1/tickets" \
  -H "Authorization: $AUTH_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "title": "Server Down - Critical",
    "description": "Production server is not responding",
    "customerId": "'$CUSTOMER_ID'",
    "projectId": "'$PROJECT_ID'",
    "ticketStatusId": "status-id",
    "ticketComplexityId": "complexity-id",
    "assignedToUserId": "'$USER_ID'",
    "isVisibleToExternals": true,
    "priority": 1
  }'
```

### Update Ticket
```bash
curl -X PUT "$BASE_URL/api/v1/tickets/$TICKET_ID" \
  -H "Authorization: $AUTH_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "title": "Server Down - RESOLVED",
    "description": "Production server is back online",
    "ticketStatusId": "resolved-status-id",
    "ticketComplexityId": "complexity-id",
    "priority": 3
  }'
```

### Delete Ticket
```bash
curl -X DELETE "$BASE_URL/api/v1/tickets/$TICKET_ID" \
  -H "Authorization: $AUTH_TOKEN"
```

## TICKET STATUS ENDPOINTS

### Get Ticket Statuses Paged
```bash
curl -X GET "$BASE_URL/api/v1/ticketstatuses?pageNumber=1&pageSize=10" \
  -H "Authorization: $AUTH_TOKEN"
```

### Create Ticket Status
```bash
curl -X POST "$BASE_URL/api/v1/ticketstatuses" \
  -H "Authorization: $AUTH_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "name": "In Progress",
    "description": "Ticket is being worked on",
    "color": "#FFA500"
  }'
```

### Update Ticket Status
```bash
curl -X PUT "$BASE_URL/api/v1/ticketstatuses/status-id" \
  -H "Authorization: $AUTH_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "name": "In Progress Updated",
    "description": "Ticket is actively being worked on",
    "color": "#FF6347"
  }'
```

### Delete Ticket Status
```bash
curl -X DELETE "$BASE_URL/api/v1/ticketstatuses/status-id" \
  -H "Authorization: $AUTH_TOKEN"
```

## TICKET COMPLEXITY ENDPOINTS

### Get Ticket Complexities Paged
```bash
curl -X GET "$BASE_URL/api/v1/ticketcomplexities?pageNumber=1&pageSize=10" \
  -H "Authorization: $AUTH_TOKEN"
```

### Create Ticket Complexity
```bash
curl -X POST "$BASE_URL/api/v1/ticketcomplexities" \
  -H "Authorization: $AUTH_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "name": "High",
    "description": "Complex issue requiring expert analysis"
  }'
```

### Update Ticket Complexity
```bash
curl -X PUT "$BASE_URL/api/v1/ticketcomplexities/complexity-id" \
  -H "Authorization: $AUTH_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "name": "Very High",
    "description": "Critical complex issue"
  }'
```

### Delete Ticket Complexity
```bash
curl -X DELETE "$BASE_URL/api/v1/ticketcomplexities/complexity-id" \
  -H "Authorization: $AUTH_TOKEN"
```

## PROJECTS ENDPOINTS

### Get Projects Paged
```bash
curl -X GET "$BASE_URL/api/v1/projects?pageNumber=1&pageSize=10&searchTerm=frontend" \
  -H "Authorization: $AUTH_TOKEN"
```

### Get Project by ID
```bash
curl -X GET "$BASE_URL/api/v1/projects/$PROJECT_ID" \
  -H "Authorization: $AUTH_TOKEN"
```

### Create Project
```bash
curl -X POST "$BASE_URL/api/v1/projects" \
  -H "Authorization: $AUTH_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "name": "Mobile App Redesign",
    "description": "Complete redesign of mobile application",
    "startDate": "2024-01-01",
    "endDate": "2024-06-30",
    "isActive": true
  }'
```

### Update Project
```bash
curl -X PUT "$BASE_URL/api/v1/projects/$PROJECT_ID" \
  -H "Authorization: $AUTH_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "name": "Mobile App Redesign v2",
    "description": "Complete redesign of mobile application - Phase 2",
    "endDate": "2024-12-31",
    "isActive": true
  }'
```

### Delete Project
```bash
curl -X DELETE "$BASE_URL/api/v1/projects/$PROJECT_ID" \
  -H "Authorization: $AUTH_TOKEN"
```

## REFERENCE DATA ENDPOINTS

### Get Countries
```bash
curl -X GET "$BASE_URL/api/v1/countries?pageNumber=1&pageSize=10" \
  -H "Authorization: $AUTH_TOKEN"
```

### Get Regions
```bash
curl -X GET "$BASE_URL/api/v1/regions?pageNumber=1&pageSize=10&countryId=country-id" \
  -H "Authorization: $AUTH_TOKEN"
```

### Get Provinces
```bash
curl -X GET "$BASE_URL/api/v1/provinces?pageNumber=1&pageSize=10&regionId=region-id" \
  -H "Authorization: $AUTH_TOKEN"
```

### Get Municipalities
```bash
curl -X GET "$BASE_URL/api/v1/municipalities?pageNumber=1&pageSize=10&provinceId=province-id" \
  -H "Authorization: $AUTH_TOKEN"
```

### Get Communication Channels
```bash
curl -X GET "$BASE_URL/api/v1/communicationchannels?pageNumber=1&pageSize=10" \
  -H "Authorization: $AUTH_TOKEN"
```

### Get Genders
```bash
curl -X GET "$BASE_URL/api/v1/genders?pageNumber=1&pageSize=10" \
  -H "Authorization: $AUTH_TOKEN"
```

### Get Identification Types
```bash
curl -X GET "$BASE_URL/api/v1/identificationtypes?pageNumber=1&pageSize=10" \
  -H "Authorization: $AUTH_TOKEN"
```

### Get Industries
```bash
curl -X GET "$BASE_URL/api/v1/industries?pageNumber=1&pageSize=10" \
  -H "Authorization: $AUTH_TOKEN"
```

### Get Entity Statuses
```bash
curl -X GET "$BASE_URL/api/v1/entitystatuses?pageNumber=1&pageSize=10" \
  -H "Authorization: $AUTH_TOKEN"
```

### Get Income Ranges
```bash
curl -X GET "$BASE_URL/api/v1/incomeranges?pageNumber=1&pageSize=10" \
  -H "Authorization: $AUTH_TOKEN"
```

### Get Tax Regimes
```bash
curl -X GET "$BASE_URL/api/v1/taxregimes?pageNumber=1&pageSize=10" \
  -H "Authorization: $AUTH_TOKEN"
```

### Get Street Types
```bash
curl -X GET "$BASE_URL/api/v1/streettypes?pageNumber=1&pageSize=10" \
  -H "Authorization: $AUTH_TOKEN"
```

### Get Areas
```bash
curl -X GET "$BASE_URL/api/v1/areas?pageNumber=1&pageSize=10" \
  -H "Authorization: $AUTH_TOKEN"
```

### Get System Modules
```bash
curl -X GET "$BASE_URL/api/v1/systemmodules?pageNumber=1&pageSize=10" \
  -H "Authorization: $AUTH_TOKEN"
```

### Get Company Modules
```bash
curl -X GET "$BASE_URL/api/v1/companymodules?pageNumber=1&pageSize=10" \
  -H "Authorization: $AUTH_TOKEN"
```

## AREAS ENDPOINTS

### Create Area
```bash
curl -X POST "$BASE_URL/api/v1/areas" \
  -H "Authorization: $AUTH_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "name": "Sales",
    "description": "Sales department"
  }'
```

### Update Area
```bash
curl -X PUT "$BASE_URL/api/v1/areas/area-id" \
  -H "Authorization: $AUTH_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "name": "Sales & Marketing",
    "description": "Sales and Marketing department"
  }'
```

### Delete Area
```bash
curl -X DELETE "$BASE_URL/api/v1/areas/area-id" \
  -H "Authorization: $AUTH_TOKEN"
```

## PERSON ADDRESS ENDPOINTS

### Get Person Addresses
```bash
curl -X GET "$BASE_URL/api/v1/person/$PERSON_ID/addresses" \
  -H "Authorization: $AUTH_TOKEN"
```

### Create Person Address
```bash
curl -X POST "$BASE_URL/api/v1/person/$PERSON_ID/addresses" \
  -H "Authorization: $AUTH_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "streetTypeId": "street-type-id",
    "streetName": "Main Street",
    "streetNumber": "123",
    "floor": "5",
    "apartment": "B",
    "additionalInfo": "Near the park",
    "zipCode": "28001",
    "municipalityId": "municipality-id",
    "countryId": "country-id"
  }'
```

### Update Person Address
```bash
curl -X PUT "$BASE_URL/api/v1/person/$PERSON_ID/addresses/address-id" \
  -H "Authorization: $AUTH_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "streetTypeId": "street-type-id",
    "streetName": "New Main Street",
    "streetNumber": "456",
    "floor": "3",
    "apartment": "A",
    "additionalInfo": "Close to subway",
    "zipCode": "28002",
    "municipalityId": "municipality-id",
    "countryId": "country-id"
  }'
```

### Delete Person Address
```bash
curl -X DELETE "$BASE_URL/api/v1/person/$PERSON_ID/addresses/address-id" \
  -H "Authorization: $AUTH_TOKEN"
```

## PERSON CONTACT ENDPOINTS

### Get Person Contacts
```bash
curl -X GET "$BASE_URL/api/v1/person/$PERSON_ID/contacts" \
  -H "Authorization: $AUTH_TOKEN"
```

### Create Person Contact
```bash
curl -X POST "$BASE_URL/api/v1/person/$PERSON_ID/contacts" \
  -H "Authorization: $AUTH_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "contactType": 1,
    "value": "john.doe@company.com",
    "communicationChannelId": "email-channel-id"
  }'
```

### Update Person Contact
```bash
curl -X PUT "$BASE_URL/api/v1/person/$PERSON_ID/contacts/contact-id" \
  -H "Authorization: $AUTH_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "contactType": 1,
    "value": "john.newemail@company.com",
    "communicationChannelId": "email-channel-id"
  }'
```

### Delete Person Contact
```bash
curl -X DELETE "$BASE_URL/api/v1/person/$PERSON_ID/contacts/contact-id" \
  -H "Authorization: $AUTH_TOKEN"
```

## PERSON BUSINESS PROFILE ENDPOINTS

### Get Person Business Profile
```bash
curl -X GET "$BASE_URL/api/v1/person/$PERSON_ID/business-profile" \
  -H "Authorization: $AUTH_TOKEN"
```

### Create Person Business Profile
```bash
curl -X POST "$BASE_URL/api/v1/person/$PERSON_ID/business-profile" \
  -H "Authorization: $AUTH_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "jobTitle": "Software Engineer",
    "areaId": "area-id",
    "businessEmail": "john@business.com",
    "businessPhone": "+1234567890"
  }'
```

### Update Person Business Profile
```bash
curl -X PUT "$BASE_URL/api/v1/person/$PERSON_ID/business-profile" \
  -H "Authorization: $AUTH_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "jobTitle": "Senior Software Engineer",
    "areaId": "area-id",
    "businessEmail": "john.senior@business.com",
    "businessPhone": "+0987654321"
  }'
```

## PERSON EMPLOYMENT ENDPOINTS

### Get Person Employments
```bash
curl -X GET "$BASE_URL/api/v1/person/$PERSON_ID/employments" \
  -H "Authorization: $AUTH_TOKEN"
```

### Create Person Employment
```bash
curl -X POST "$BASE_URL/api/v1/person/$PERSON_ID/employments" \
  -H "Authorization: $AUTH_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "companyId": "'$COMPANY_ID'",
    "positionTitle": "Developer",
    "startDate": "2020-01-15",
    "endDate": "2024-01-15",
    "currentlyEmployed": false
  }'
```

### Update Person Employment
```bash
curl -X PUT "$BASE_URL/api/v1/person/$PERSON_ID/employments/employment-id" \
  -H "Authorization: $AUTH_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "positionTitle": "Senior Developer",
    "startDate": "2020-01-15",
    "endDate": null,
    "currentlyEmployed": true
  }'
```

## PERSON FINANCIAL PROFILE ENDPOINTS

### Get Person Financial Profile
```bash
curl -X GET "$BASE_URL/api/v1/person/$PERSON_ID/financial-profile" \
  -H "Authorization: $AUTH_TOKEN"
```

### Create Person Financial Profile
```bash
curl -X POST "$BASE_URL/api/v1/person/$PERSON_ID/financial-profile" \
  -H "Authorization: $AUTH_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "incomeRangeId": "income-range-id",
    "taxRegimeId": "tax-regime-id",
    "creditLimit": 50000,
    "creditAvailable": 50000,
    "paymentTermsDays": 30
  }'
```

### Update Person Financial Profile
```bash
curl -X PUT "$BASE_URL/api/v1/person/$PERSON_ID/financial-profile" \
  -H "Authorization: $AUTH_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "incomeRangeId": "income-range-id",
    "taxRegimeId": "tax-regime-id",
    "creditLimit": 100000,
    "creditAvailable": 75000,
    "paymentTermsDays": 60
  }'
```

## TICKET COMPANY DEFAULTS ENDPOINTS

### Get Ticket Company Defaults Paged
```bash
curl -X GET "$BASE_URL/api/v1/ticketcompanydefaults?pageNumber=1&pageSize=10" \
  -H "Authorization: $AUTH_TOKEN"
```

### Create Ticket Company Default
```bash
curl -X POST "$BASE_URL/api/v1/ticketcompanydefaults" \
  -H "Authorization: $AUTH_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "ticketStatusId": "status-id",
    "ticketComplexityId": "complexity-id",
    "defaultAssignedUserId": "'$USER_ID'"
  }'
```

### Update Ticket Company Default
```bash
curl -X PUT "$BASE_URL/api/v1/ticketcompanydefaults/default-id" \
  -H "Authorization: $AUTH_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "ticketStatusId": "status-id",
    "ticketComplexityId": "complexity-id",
    "defaultAssignedUserId": "different-user-id"
  }'
```

### Delete Ticket Company Default
```bash
curl -X DELETE "$BASE_URL/api/v1/ticketcompanydefaults/default-id" \
  -H "Authorization: $AUTH_TOKEN"
```

## TIME UNITS ENDPOINTS

### Get Time Units Paged
```bash
curl -X GET "$BASE_URL/api/v1/timeunits?pageNumber=1&pageSize=10" \
  -H "Authorization: $AUTH_TOKEN"
```

### Create Time Unit
```bash
curl -X POST "$BASE_URL/api/v1/timeunits" \
  -H "Authorization: $AUTH_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "name": "Hour",
    "abbreviation": "h",
    "factor": 1
  }'
```

### Update Time Unit
```bash
curl -X PUT "$BASE_URL/api/v1/timeunits/timeunit-id" \
  -H "Authorization: $AUTH_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "name": "Working Hour",
    "abbreviation": "wh",
    "factor": 1
  }'
```

### Delete Time Unit
```bash
curl -X DELETE "$BASE_URL/api/v1/timeunits/timeunit-id" \
  -H "Authorization: $AUTH_TOKEN"
```

## ROLES ENDPOINTS

### Get Roles Paged
```bash
curl -X GET "$BASE_URL/api/v1/roles?pageNumber=1&pageSize=10" \
  -H "Authorization: $AUTH_TOKEN"
```

### Create Role
```bash
curl -X POST "$BASE_URL/api/v1/roles" \
  -H "Authorization: $AUTH_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "name": "Support Manager",
    "description": "Manages support tickets and teams"
  }'
```

### Update Role
```bash
curl -X PUT "$BASE_URL/api/v1/roles/role-id" \
  -H "Authorization: $AUTH_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "name": "Senior Support Manager",
    "description": "Manages support tickets, teams, and escalations"
  }'
```

### Delete Role
```bash
curl -X DELETE "$BASE_URL/api/v1/roles/role-id" \
  -H "Authorization: $AUTH_TOKEN"
```

## USERS ENDPOINTS

### Get Users Paged
```bash
curl -X GET "$BASE_URL/api/v1/users?pageNumber=1&pageSize=10&email=user@example.com" \
  -H "Authorization: $AUTH_TOKEN"
```

### Get User by ID
```bash
curl -X GET "$BASE_URL/api/v1/users/$USER_ID" \
  -H "Authorization: $AUTH_TOKEN"
```

### Create User
```bash
curl -X POST "$BASE_URL/api/v1/users" \
  -H "Authorization: $AUTH_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "firstName": "Jane",
    "lastName": "Smith",
    "email": "jane.smith@company.com",
    "roleId": "role-id"
  }'
```

### Update User
```bash
curl -X PUT "$BASE_URL/api/v1/users/$USER_ID" \
  -H "Authorization: $AUTH_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{
    "firstName": "Jane",
    "lastName": "Smith Updated",
    "email": "jane.smith.updated@company.com",
    "roleId": "role-id"
  }'
```

### Delete User
```bash
curl -X DELETE "$BASE_URL/api/v1/users/$USER_ID" \
  -H "Authorization: $AUTH_TOKEN"
```
