#include "model.h"
#include "idl/heap.h"
#include "idl/string.h"
#include <stdlib.h>
#include <string.h>
#include <stdio.h>
#include <inttypes.h>

#ifndef _WIN32
#define strtok_s strtok_r
#endif

dm_rec_t* dm_sources = NULL;
dm_rec_t* dm_types = NULL;
dm_rec_t* dm_last_struct = NULL;
dm_rec_t* dm_last_enum = NULL;

dm_rec_t* dm_new(void) {
    return (dm_rec_t*)calloc(1, sizeof(dm_rec_t));
}

dm_rec_t* dm_add(dm_rec_t** list, dm_rec_t* item) {
    if (!list || !item) return NULL;
    
    if (*list == NULL) {
        *list = item;
        return item;
    }
    
    dm_rec_t* p = *list;
    while (p->next) {
        p = p->next;
    }
    p->next = item;
    return item;
}

dm_rec_t* dm_find_by_name(dm_rec_t* list, const char* name) {
    if (!name) return NULL;
    
    for (dm_rec_t* p = list; p != NULL; p = p->next) {
        if (p->name && strcmp(p->name, name) == 0) {
            return p;
        }
    }
    return NULL;
}

dm_rec_t* dm_find_by_c_name(dm_rec_t* list, const char* c_name) {
    if (!c_name) return NULL;
    
    for (dm_rec_t* p = list; p != NULL; p = p->next) {
        if (p->c_name && strcmp(p->c_name, c_name) == 0) {
            return p;
        }
    }
    return NULL;
}

static dm_rec_t* find_member_by_name(dm_rec_t* type_rec, const char* name) {
    if (!type_rec || !name) return NULL;
    
    for (dm_rec_t* m = type_rec->members; m != NULL; m = m->next) {
        if (m->name && strcmp(m->name, name) == 0) {
            return m;
        }
    }
    return NULL;
}

static size_t get_primitive_size_align(const char* type_name) {
    if (!type_name) return 0;
    
    if (strcmp(type_name, "boolean") == 0) return 1;
    if (strcmp(type_name, "char") == 0) return 1;
    if (strcmp(type_name, "octet") == 0) return 1;
    if (strcmp(type_name, "short") == 0) return 2;
    if (strcmp(type_name, "unsigned short") == 0) return 2;
    if (strcmp(type_name, "long") == 0) return 4;
    if (strcmp(type_name, "unsigned long") == 0) return 4;
    if (strcmp(type_name, "long long") == 0) return 8;
    if (strcmp(type_name, "unsigned long long") == 0) return 8;
    if (strcmp(type_name, "float") == 0) return 4;
    if (strcmp(type_name, "double") == 0) return 8;
    if (strcmp(type_name, "long double") == 0) return 16;
    if (strcmp(type_name, "string") == 0) return 8; // pointer
    if (strcmp(type_name, "wstring") == 0) return 8; // pointer
    
    return 0;
}

static uint32_t align_up(uint32_t offset, size_t alignment) {
    if (alignment == 0) return offset;
    size_t remainder = offset % alignment;
    return (remainder == 0) ? offset : offset + (alignment - remainder);
}

void dm_calculate_layout(dm_rec_t* struct_rec) {
    if (!struct_rec || !struct_rec->members) return;
    
    uint32_t cursor = 0;
    uint32_t max_align = 1;
    uint32_t union_max_size = 0;
    
    int is_union = (struct_rec->kind && strcmp(struct_rec->kind, "union") == 0);
    
    for (dm_rec_t* member = struct_rec->members; member != NULL; member = member->next) {
        size_t member_size = 0;
        size_t member_align = 1;
        
        // Check primitive types
        size_t prim_size = get_primitive_size_align(member->type);
        
        if (prim_size > 0) {
            member_size = prim_size;
            member_align = (prim_size >= 8) ? 8 : prim_size;
        } else {
            // Complex type - lookup
            dm_rec_t* nested = dm_find_by_name(dm_types, member->type);
            
            if (nested && nested->size > 0) {
                member_size = nested->size;
                member_align = nested->align;
            } else if ((member->kind && strcmp(member->kind, "sequence") == 0) || (member->type && strstr(member->type, "sequence"))) {
                // DDS sequence: {uint32, uint32, T*, bool} = 24 bytes aligned to 8 (approx, arch dependent)
                // Assuming 64-bit for now as per guide
                member_size = 24;
                member_align = 8;
            } else {
                // Assume enum or unknown (4-byte aligned)
                member_size = 4;
                member_align = 4;
            }
        }
        
        // Handle arrays
        if (member->is_array && member->size > 0) {
            member_size *= member->size;
        }
        
        // Apply padding
        if (!is_union) {
            cursor = align_up(cursor, member_align);
        } else {
            cursor = 0;  // Union members overlay
        }
        
        member->offset = cursor;
        
        if (!is_union) {
            cursor += member_size;
        } else {
            if (member_size > union_max_size) union_max_size = member_size;
            // cursor tracks max size for union? No, cursor is used for offset for NEXT.
            // But for union, offset is always 0 (handled above).
            // So we just track max size.
            cursor = union_max_size;
        }
        
        if (member_align > max_align) {
            max_align = member_align;
        }
    }
    
    // Final padding
    struct_rec->size = align_up(cursor, max_align);
    struct_rec->align = max_align;
}

int dm_get_member_offset(const char* type_c_name, const char* member_name) {
    if (!type_c_name || !member_name) return 0;
    
    dm_rec_t* type_rec = dm_find_by_c_name(dm_types, type_c_name);
    if (!type_rec) {
        // Fallback: try IDL name
        type_rec = dm_find_by_name(dm_types, type_c_name);
        if (!type_rec) return 0;
    }
    
    // Handle nested paths (e.g., "ProcessAddr.StationId")
    char* path_copy = idl_strdup(member_name);
    char* saveptr = NULL;
    char* token = strtok_s(path_copy, ".", &saveptr);
    dm_rec_t* current_type = type_rec;
    int current_offset = 0;
    
    while (token != NULL) {
        dm_rec_t* member = find_member_by_name(current_type, token);
        if (!member) {
            free(path_copy);
            return 0;
        }
        
        current_offset += member->offset;
        
        token = strtok_s(NULL, ".", &saveptr);
        if (token) {
            // Navigate to member's type
            current_type = dm_find_by_name(dm_types, member->type);
            if (!current_type) {
                free(path_copy);
                return 0;
            }
        }
    }
    
    free(path_copy);
    return current_offset;
}

static const char* dm_escapize(const char* s) {
    static char buf[2048];
    buf[0] = '\0';
    if (!s) return buf;
    
    const char* c = s;
    char* d = buf;
    
    while (*c && (d - buf) < 2046) {
        if (*c == '\\' || *c == '"') {
            *d++ = '\\';
        }
        *d++ = *c++;
    }
    *d = '\0';
    
    return buf;
}

static void dm_indent(FILE* fh, int indent) {
    for (int i = 0; i < indent * 2; i++) {
        fprintf(fh, " ");
    }
}

static void dm_print_value(FILE* fh, dm_rec_t* rec, int indent) {
    if (!rec->has_value) return;
    
    dm_indent(fh, indent);
    
    switch (rec->value_type) {
        case DM_TYPE_BOOL:
            fprintf(fh, "\"Value\": %s,\n", rec->value.bln ? "true" : "false");
            break;
        case DM_TYPE_INT:
            fprintf(fh, "\"Value\": %" PRId64 ",\n", rec->value.int64);
            break;
        case DM_TYPE_UNSIGNED_INT:
            fprintf(fh, "\"Value\": %" PRIu64 ",\n", rec->value.uint64);
            break;
        case DM_TYPE_DOUBLE:
            fprintf(fh, "\"Value\": %lf,\n", rec->value.dbl);
            break;
        case DM_TYPE_LONG_DOUBLE:
            fprintf(fh, "\"Value\": %Lf,\n", rec->value.ldbl);
            break;
        case DM_TYPE_STRING:
            fprintf(fh, "\"Value\": \"%s\",\n", dm_escapize(rec->value.str));
            break;
    }
}

static void dm_print_labels(FILE* fh, dm_rec_t* labels, int indent) {
    if (!labels) return;
    
    dm_indent(fh, indent);
    fprintf(fh, "\"Labels\": [\n");
    
    for (dm_rec_t* l = labels; l; l = l->next) {
        dm_indent(fh, indent + 1);
        fprintf(fh, "\"%s\"", dm_escapize(l->name));
        if (l->next) fprintf(fh, ",");
        fprintf(fh, "\n");
    }
    
    dm_indent(fh, indent);
    fprintf(fh, "],\n");
}

static void dm_print_qos(FILE* fh, dm_qos_t* qos, int indent) {
    if (!qos) return;
    
    dm_indent(fh, indent);
    fprintf(fh, "\"QoS\": {\n");
    
    if (qos->reliability) {
        dm_indent(fh, indent + 1);
        fprintf(fh, "\"Reliability\": \"%s\",\n", qos->reliability);
    }
    
    if (qos->durability) {
        dm_indent(fh, indent + 1);
        fprintf(fh, "\"Durability\": \"%s\",\n", qos->durability);
    }
    
    if (qos->history) {
        dm_indent(fh, indent + 1);
        fprintf(fh, "\"History\": \"%s\",\n", qos->history);
    }
    
    dm_indent(fh, indent + 1);
    fprintf(fh, "\"HistoryDepth\": %d\n", qos->depth);
    
    dm_indent(fh, indent);
    fprintf(fh, "},\n");
}

static void dm_print_descriptor(FILE* fh, dm_descriptor_t* desc, int indent) {
    if (!desc) return;
    
    dm_indent(fh, indent);
    fprintf(fh, "\"TopicDescriptor\": {\n");
    
    dm_indent(fh, indent + 1);
    fprintf(fh, "\"Size\": %u,\n", desc->size);
    
    dm_indent(fh, indent + 1);
    fprintf(fh, "\"Align\": %u,\n", desc->align);
    
    dm_indent(fh, indent + 1);
    fprintf(fh, "\"FlagSet\": %u,\n", desc->flagset);
    
    dm_indent(fh, indent + 1);
    fprintf(fh, "\"TypeName\": \"%s\",\n", desc->typename ? desc->typename : "");
    
    // Keys
    dm_indent(fh, indent + 1);
    fprintf(fh, "\"Keys\": [\n");
    for (uint32_t i = 0; i < desc->n_keys; i++) {
        dm_indent(fh, indent + 2);
        fprintf(fh, "{ \"Name\": \"%s\", \"Offset\": %u, \"Order\": %u }",
                desc->keys[i].name,
                desc->keys[i].offset,
                desc->keys[i].order);
        if (i < desc->n_keys - 1) fprintf(fh, ",");
        fprintf(fh, "\n");
    }
    dm_indent(fh, indent + 1);
    fprintf(fh, "],\n");
    
    // Ops
    dm_indent(fh, indent + 1);
    fprintf(fh, "\"Ops\": [\n");
    for (uint32_t i = 0; i < desc->n_ops; i++) {
        if (i % 8 == 0) dm_indent(fh, indent + 2);
        fprintf(fh, "%u", desc->ops[i]);
        if (i < desc->n_ops - 1) fprintf(fh, ", ");
        if ((i + 1) % 8 == 0 || i == desc->n_ops - 1) fprintf(fh, "\n");
    }
    dm_indent(fh, indent + 1);
    fprintf(fh, "]\n");
    
    dm_indent(fh, indent);
    fprintf(fh, "},\n");
}

static void dm_print_list(FILE* fh, dm_rec_t* list, int indent);

static void dm_print_rec(FILE* fh, dm_rec_t* rec, int indent) {
    if (!rec) return;
    
    dm_indent(fh, indent);
    fprintf(fh, "{\n");
    
    // Identity
    if (rec->name) {
        dm_indent(fh, indent + 1);
        fprintf(fh, "\"Name\": \"%s\",\n", dm_escapize(rec->name));
    }
    
    if (rec->kind) {
        dm_indent(fh, indent + 1);
        fprintf(fh, "\"Kind\": \"%s\",\n", dm_escapize(rec->kind));
    }
    
    if (rec->type) {
        dm_indent(fh, indent + 1);
        fprintf(fh, "\"Type\": \"%s\",\n", dm_escapize(rec->type));
    }
    
    // Annotations
    if (rec->extensibility) {
        dm_indent(fh, indent + 1);
        fprintf(fh, "\"Extensibility\": \"%s\",\n", rec->extensibility);
    }
    
    if (rec->discriminator) {
        dm_indent(fh, indent + 1);
        fprintf(fh, "\"Discriminator\": \"%s\",\n", dm_escapize(rec->discriminator));
    }
    
    if (rec->is_key) {
        dm_indent(fh, indent + 1);
        fprintf(fh, "\"IsKey\": true,\n");
    }
    
    if (rec->has_explicit_id) {
        dm_indent(fh, indent + 1);
        fprintf(fh, "\"Id\": %d,\n", rec->member_id);
    }
    
    if (rec->is_optional) {
        dm_indent(fh, indent + 1);
        fprintf(fh, "\"IsOptional\": true,\n");
    }
    
    if (rec->is_external) {
        dm_indent(fh, indent + 1);
        fprintf(fh, "\"IsExternal\": true,\n");
    }
    
    if (rec->bound > 0) {
        dm_indent(fh, indent + 1);
        fprintf(fh, "\"Bound\": %u,\n", rec->bound);
    }
    
    if (rec->is_array) {
        dm_indent(fh, indent + 1);
        fprintf(fh, "\"CollectionType\": \"array\",\n");
        if (rec->size > 0) {
            dm_indent(fh, indent + 1);
            fprintf(fh, "\"Size\": %d,\n", rec->size);
        }
    } else if (rec->kind && strcmp(rec->kind, "sequence") == 0) {
        dm_indent(fh, indent + 1);
        fprintf(fh, "\"CollectionType\": \"sequence\",\n");
    }
    
    // Labels
    if (rec->labels) {
        dm_print_labels(fh, rec->labels, indent + 1);
    }
    
    // Value
    if (rec->has_value) {
        dm_print_value(fh, rec, indent + 1);
    }
    
    // QoS
    if (rec->qos) {
        dm_print_qos(fh, rec->qos, indent + 1);
    }
    
    // Descriptor
    if (rec->topic_descriptor) {
        dm_print_descriptor(fh, rec->topic_descriptor, indent + 1);
    }
    
    // Members
    if (rec->members) {
        dm_indent(fh, indent + 1);
        fprintf(fh, "\"Members\":\n");
        dm_print_list(fh, rec->members, indent + 1);
        fprintf(fh, ",\n");
    }
    
    // EOF marker
    dm_indent(fh, indent + 1);
    fprintf(fh, "\"_eof\": 0\n");
    
    dm_indent(fh, indent);
    fprintf(fh, "}");
}

static void dm_print_list(FILE* fh, dm_rec_t* list, int indent) {
    if (!list) {
        fprintf(fh, "null"); 
        return; 
    }

    dm_indent(fh, indent);
    fprintf(fh, "[\n");
    
    for (dm_rec_t* rec = list; rec != NULL; rec = rec->next) {
        dm_print_rec(fh, rec, indent + 1);
        if (rec->next) fprintf(fh, ",");
        fprintf(fh, "\n");
    }
    
    dm_indent(fh, indent);
    fprintf(fh, "]");
}

void dm_fprint(FILE* fh) {
    fprintf(fh, "{\n");
    fprintf(fh, "  \"File\":\n");
    dm_print_list(fh, dm_sources, 2);
    fprintf(fh, ",\n");
    fprintf(fh, "  \"Types\":\n");
    dm_print_list(fh, dm_types, 2);
    fprintf(fh, "\n}\n");
}
