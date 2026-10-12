import { CrudConfig, CrudField } from './crud';



/** List-and-form pages that are plain setup data. Each is rendered by `CrudPage`. */



const active: CrudField = { key: 'isActive', label: 'Active', type: 'checkbox', default: true };



export const departments: CrudConfig = {

    title: 'Departments',

    subtitle: 'The departments employees belong to.',

    noun: 'department',

    endpoint: '/api/org/departments',

    writePermission: 'org.setup.write',

    canDelete: true,

    columns: [

        { key: 'name', label: 'Name' },

        { key: 'isActive', label: 'Active', kind: 'bool' }

    ],

    fields: [{ key: 'name', label: 'Name', type: 'text', required: true, maxLength: 150, wide: true }, active]

};



export const designations: CrudConfig = {

    ...departments,

    title: 'Designations',

    subtitle: 'Job titles used on employee records.',

    noun: 'designation',

    endpoint: '/api/org/designations'

};



export const branches: CrudConfig = {

    title: 'Branches',

    subtitle: 'Offices and sites of the company. Work locations and employees can be tied to a branch.',

    noun: 'branch',

    endpoint: '/api/platform/branches',

    writePermission: 'platform.branches.write',

    columns: [

        { key: 'code', label: 'Code' },

        { key: 'name', label: 'Name' },

        { key: 'address', label: 'Address' },

        { key: 'phone', label: 'Phone' },

        { key: 'isActive', label: 'Active', kind: 'bool' }

    ],

    fields: [

        { key: 'code', label: 'Code', type: 'text', required: true, maxLength: 40, hint: 'Letters, digits and dashes.' },

        { key: 'name', label: 'Name', type: 'text', required: true, maxLength: 150 },

        { key: 'address', label: 'Address', type: 'textarea', maxLength: 500 },

        { key: 'phone', label: 'Phone', type: 'text', maxLength: 30 },

        active

    ]

};



export const employees: CrudConfig = {

    title: 'Employees',

    subtitle: 'Link an employee to a user account so they can record their own attendance and apply for leave.',

    noun: 'employee',

    endpoint: '/api/org/employees',

    writePermission: 'org.employees.write',

    paged: true,

    columns: [

        { key: 'code', label: 'Code' },

        { key: 'fullName', label: 'Name' },

        { key: 'department', label: 'Department' },

        { key: 'designation', label: 'Designation' },

        { key: 'branch', label: 'Branch' },

        { key: 'reportsTo', label: 'Reports to' },

        { key: 'userLogin', label: 'User account' },

        { key: 'isActive', label: 'Active', kind: 'bool' }

    ],

    fields: [

        { key: 'code', label: 'Employee code', type: 'text', maxLength: 50, hint: 'Leave blank to use the next code (EMP0001, …).' },

        { key: 'fullName', label: 'Full name', type: 'text', required: true, maxLength: 200 },

        { key: 'gender', label: 'Gender', type: 'select', options: ['Male', 'Female', 'Other'].map((x) => ({ label: x, value: x })) },

        { key: 'dateOfBirth', label: 'Date of birth', type: 'date' },

        { key: 'phone', label: 'Phone', type: 'text', maxLength: 30 },

        { key: 'email', label: 'Email', type: 'email', maxLength: 254 },

        { key: 'joiningDate', label: 'Joining date', type: 'date' },

        { key: 'exitDate', label: 'Exit date', type: 'date' },

        { key: 'employmentType', label: 'Employment type', type: 'select', options: ['Permanent', 'Probation', 'Contract', 'Intern', 'Part-time'].map((x) => ({ label: x, value: x })) },

        { key: 'branchId', label: 'Branch', type: 'select', lookup: 'branches' },

        { key: 'departmentId', label: 'Department', type: 'select', lookup: 'departments' },

        { key: 'designationId', label: 'Designation', type: 'select', lookup: 'designations' },

        { key: 'reportsToId', label: 'Reports to (line manager)', type: 'select', lookup: 'employees', hint: 'The line manager takes the first leave approval step.' },

        { key: 'userId', label: 'User account', type: 'select', lookup: 'unlinkedUsers', currentLabelKey: 'userLogin', hint: 'Create the account under Administration → Users first.' },

        active

    ]

};



export const workLocations: CrudConfig = {

    title: 'Work Locations',

    subtitle: 'Geo-fences for attendance. A check-in counts as at work when it is inside a location’s radius.',

    noun: 'work location',

    endpoint: '/api/attendance/locations',

    writePermission: 'attendance.setup.write',

    canDelete: true,

    locate: true,

    columns: [

        { key: 'name', label: 'Name' },

        { key: 'locationType', label: 'Type', kind: 'label' },

        { key: 'branch', label: 'Branch' },

        { key: 'latitude', label: 'Latitude' },

        { key: 'longitude', label: 'Longitude' },

        { key: 'radiusMeters', label: 'Radius (m)' },

        { key: 'isActive', label: 'Active', kind: 'bool' }

    ],

    fields: [

        { key: 'name', label: 'Name', type: 'text', required: true, maxLength: 150 },

        {

            key: 'locationType',

            label: 'Type',

            type: 'select',

            required: true,

            default: 'Office',

            options: [

                { label: 'Office', value: 'Office' },

                { label: 'Client or project site', value: 'Site' },

                { label: 'Remote work allowed', value: 'RemoteAllowed' }

            ]

        },

        { key: 'latitude', label: 'Latitude', type: 'number', min: -90, max: 90, step: 0.000001 },

        { key: 'longitude', label: 'Longitude', type: 'number', min: -180, max: 180, step: 0.000001 },

        { key: 'radiusMeters', label: 'Radius (metres)', type: 'number', required: true, default: 150, min: 10, max: 100000, step: 1, hint: 'Allow for GPS drift: 100–200 m suits most offices.' },

        { key: 'branchId', label: 'Branch', type: 'select', lookup: 'branches' },

        { key: 'allowedPublicIps', label: 'Office network IP addresses', type: 'text', maxLength: 500, wide: true, hint: 'Optional, comma separated. A check-in from these addresses counts as at this location even when GPS is unavailable.' },

        { key: 'address', label: 'Address', type: 'textarea', maxLength: 500 },

        active

    ]

};



export const holidays: CrudConfig = {

    title: 'Holidays',

    subtitle: 'Company holidays. Nobody is marked absent on a holiday.',

    noun: 'holiday',

    endpoint: '/api/attendance/holidays',

    writePermission: 'attendance.setup.write',

    canDelete: true,

    columns: [

        { key: 'holidayDate', label: 'Date', kind: 'date' },

        { key: 'title', label: 'Holiday' },

        { key: 'remarks', label: 'Remarks' }

    ],

    fields: [

        { key: 'title', label: 'Holiday', type: 'text', required: true, maxLength: 200 },

        { key: 'holidayDate', label: 'Date', type: 'date', required: true },

        { key: 'remarks', label: 'Remarks', type: 'textarea', maxLength: 500 }

    ]

};



export const leaveTypes: CrudConfig = {

    title: 'Leave Types',

    subtitle: 'The kinds of leave employees can apply for and how many days each allows per year.',

    noun: 'leave type',

    endpoint: '/api/leave/types',

    listParams: { includeInactive: true },

    writePermission: 'leave.setup.write',

    columns: [

        { key: 'code', label: 'Code' },

        { key: 'name', label: 'Name' },

        { key: 'yearlyQuota', label: 'Days per year' },

        { key: 'isPaid', label: 'Paid', kind: 'bool' },

        { key: 'isActive', label: 'Active', kind: 'bool' }

    ],

    fields: [

        { key: 'code', label: 'Code', type: 'text', required: true, maxLength: 20 },

        { key: 'name', label: 'Name', type: 'text', required: true, maxLength: 100 },

        { key: 'yearlyQuota', label: 'Days per year', type: 'number', min: 0, max: 366, step: 0.5, hint: 'Leave blank for no limit.' },

        { key: 'isPaid', label: 'Paid leave', type: 'checkbox', default: true },

        active

    ]

};



export const leaveAdjustments: CrudConfig = {

    title: 'Balance Adjustments',

    subtitle: 'Add to or take from an employee’s yearly leave balance, for example days carried forward.',

    noun: 'adjustment',

    endpoint: '/api/leave/adjustments',

    writePermission: 'leave.adjust',

    canEdit: false,

    canDelete: true,

    columns: [

        { key: 'employeeName', label: 'Employee' },

        { key: 'leaveType', label: 'Leave type' },

        { key: 'year', label: 'Year' },

        { key: 'days', label: 'Days' },

        { key: 'reason', label: 'Reason' }

    ],

    fields: [

        { key: 'employeeId', label: 'Employee', type: 'select', lookup: 'employees', required: true },

        { key: 'leaveTypeId', label: 'Leave type', type: 'select', lookup: 'leaveTypes', required: true },

        { key: 'year', label: 'Year', type: 'number', required: true, default: new Date().getFullYear(), min: 2000, max: 2200, step: 1 },

        { key: 'days', label: 'Days', type: 'number', required: true, step: 0.5, hint: 'Positive adds days, negative takes them away.' },

        { key: 'reason', label: 'Reason', type: 'textarea', maxLength: 500 }

    ]

};
