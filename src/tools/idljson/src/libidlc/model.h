#ifndef MODEL_H
#define MODEL_H

#include <stdio.h>
#include <stdint.h>

// Value type enumeration
enum dm_type {
    DM_TYPE_BOOL,
    DM_TYPE_INT,
    DM_TYPE_UNSIGNED_INT,
    DM_TYPE_DOUBLE,
    DM_TYPE_LONG_DOUBLE,
    DM_TYPE_STRING,
};

// Value union
typedef union dm_value {
    uint8_t bln;
    uint64_t uint64;
    int64_t int64;
    double dbl;
    long double ldbl;
    char* str;
} dm_value_t;

// QoS settings
typedef struct dm_qos {
    char* reliability;
    char* durability;
    char* history;
    int32_t depth;
} dm_qos_t;

// Topic descriptor
typedef struct dm_descriptor {
    uint32_t size;
    uint32_t align;
    uint32_t flagset;
    char* typename;
    
    struct {
        char* name;
        uint32_t offset;
        uint32_t order;
    } *keys;
    uint32_t n_keys;
    
    uint32_t* ops;
    uint32_t n_ops;
} dm_descriptor_t;

// Type record
typedef struct dm_rec {
    // Identity
    char* name;
    char* c_name;
    char* kind;
    char* type;
    
    // Metadata
    int is_key;
    int member_id;
    int has_explicit_id;
    int is_optional;
    int is_external;
    char* extensibility;
    
    // Collections
    int is_array;
    int size;
    uint32_t bound;
    
    // Layout
    int offset;
    int align;
    
    // Values
    int has_value;
    enum dm_type value_type;
    union dm_value value;
    
    // Relationships
    struct dm_rec* members;
    struct dm_rec* next;
    
    // Union specific
    char* discriminator;
    struct dm_rec* labels;
    
    // Topic specific
    dm_descriptor_t* topic_descriptor;
    dm_qos_t* qos;
} dm_rec_t;

// Global state
extern dm_rec_t* dm_sources;
extern dm_rec_t* dm_types;
extern dm_rec_t* dm_last_struct;
extern dm_rec_t* dm_last_enum;

// Functions
extern dm_rec_t* dm_new(void);
extern dm_rec_t* dm_add(dm_rec_t** list, dm_rec_t* item);
extern void dm_fprint(FILE* fh);
extern void dm_calculate_layout(dm_rec_t* struct_rec);
extern int dm_get_member_offset(const char* type_c_name, const char* member_name);
extern dm_rec_t* dm_find_by_name(dm_rec_t* list, const char* name);
extern dm_rec_t* dm_find_by_c_name(dm_rec_t* list, const char* c_name);

#endif /* MODEL_H */
