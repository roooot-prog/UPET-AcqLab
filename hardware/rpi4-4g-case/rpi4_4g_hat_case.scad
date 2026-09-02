// =============================================================================
//  Raspberry Pi 4 + 4G HAT enclosure  (router-style, 2x SMA paddle antennas)
//  Target HAT : Waveshare SIM7600G-H / SIM7600E-H (56.21 x 65.15 mm) or similar
//
//  OpenSCAD 2021.01+
//
//  Export:
//    openscad -D part=\"base\"      -o rpi4_4g_case_base.stl      rpi4_4g_hat_case.scad
//    openscad -D part=\"lid\"       -o rpi4_4g_case_lid.stl       rpi4_4g_hat_case.scad
//    openscad -D part=\"print_set\" -o rpi4_4g_case_print_set.stl rpi4_4g_hat_case.scad
// =============================================================================

part = "preview";   // preview | base | lid | print_set

/* ------------------------- user-tunable parameters ------------------------- */

wall            = 2.5;
floor_t         = 2.6;
lid_t           = 2.2;     // keep <= 2.2 so stock SMA bulkhead nuts fully engage
lip_h           = 2.4;
lip_t           = 1.4;
fit_clear       = 0.30;    // lid lip clearance (increase if the lid is tight)

sd_clearance    = 14;      // space at SD-card end (posts + SD access)
hdmi_clearance  = 4.0;     // Pi USB-C / HDMI / audio almost at the wall
usb_clearance   = 4.5;     // USB/ETH slightly recessed (protected)
gpio_clearance  = 14;      // space at GPIO end for posts + SMA pigtails

standoff_h      = 3.2;     // Pi bottom clearance (SD card + solder)
pcb_t           = 1.5;
hat_stack       = 26;      // GPIO header + HAT PCB + SIM7600 can + cables
                           // raise to 30 if your HAT is taller (big electrolytic)

corner_r        = 3.5;
$fn             = 48;

sma_enable      = true;
sma_spacing     = 50;      // MAIN ↔ AUX, mm (router-like)
sma_hole_d      = 6.55;    // SMA bulkhead 1/4-36
sma_flat        = 6.05;    // D-hole anti-rotation flat
gps_knockout    = true;    // thin 0.45 mm disk you can punch for a 3rd SMA (GNSS)

vent_enable     = true;
wall_mount      = true;
label_text      = "4G";

/* ----------------------------- derived geometry ---------------------------- */

pi_x = 85.0;
pi_y = 56.0;

inner_x = sd_clearance + pi_x + usb_clearance;
inner_y = hdmi_clearance + pi_y + gpio_clearance;

pi_ox = sd_clearance;
pi_oy = hdmi_clearance;

pcb_z     = floor_t + standoff_h;          // PCB bottom
pcb_top   = pcb_z + pcb_t;
cavity_h  = standoff_h + pcb_t + hat_stack;
base_h    = floor_t + cavity_h;            // mating face of the base
outer_x   = inner_x + 2 * wall;
outer_y   = inner_y + 2 * wall;
outer_z   = base_h + lid_t;                // overall body height (lip overlaps)

eps = 0.12;

// Official Raspberry Pi 4 mounting holes (origin = SD/HDMI corner of the PCB)
pi_holes = [[3.5, 3.5], [61.5, 3.5], [3.5, 52.5], [61.5, 52.5]];

// Lid screws live only in the SD and GPIO margins (USB/HDMI walls are full of ports)
lid_screws = [
    [6.0,            12.0],
    [6.0,            inner_y - 6.0],
    [inner_x * 0.50, inner_y - 6.0],
    [inner_x - 6.0,  inner_y - 6.0]
];

sma_y  = inner_y - 6.5;
sma_x1 = inner_x / 2 - sma_spacing / 2;
sma_x2 = inner_x / 2 + sma_spacing / 2;
sma_x3 = inner_x / 2;                      // GPS knockout, offset toward SD
gps_x  = sd_clearance + 18;
gps_y  = inner_y - 6.5;

/* --------------------------------- helpers -------------------------------- */

module inner_2d() {
    offset(r = corner_r) offset(delta = -corner_r)
        square([inner_x, inner_y], center = false);
}

module outer_2d() {
    offset(delta = wall) inner_2d();
}

module hex_af(af, h) {
    cylinder(d = af / cos(30), h = h, $fn = 6);
}

module sma_d_hole(h) {
    intersection() {
        cylinder(d = sma_hole_d, h = h);
        translate([-5, -sma_hole_d / 2, 0])
            cube([10, sma_flat, h]);
    }
}

module grill(sx, sy, bar = 1.4, gap = 2.2, t = 8) {
    pitch = bar + gap;
    nx = max(1, floor((sx - bar) / pitch));
    ny = max(1, floor((sy - bar) / pitch));
    ox = (sx - (nx * pitch - gap)) / 2;
    oy = (sy - (ny * pitch - gap)) / 2;
    for (i = [0 : nx - 1], j = [0 : ny - 1])
        translate([ox + i * pitch, oy + j * pitch, -eps])
            cube([gap, gap, t]);
}

module foot_recess() {
    cylinder(d = 11.0, h = 1.0);
}

/* ------------------------------ Pi 4 cutouts ------------------------------- */
// Port centres from the official Raspberry Pi 4 mechanical drawing, PCB origin
// at the SD-card / HDMI corner. USB/Ethernet sit on X = 85, HDMI-side on Y = 0.

module pi_port_cutouts() {
    // USB-C power  (bottom / HDMI edge)
    translate([pi_ox + 11.2, pi_oy, pcb_z + 1.4])
        cube([10.2, 20, 4.6], center = true);

    // micro-HDMI 0 / 1
    for (cx = [26.0, 39.5])
        translate([pi_ox + cx, pi_oy, pcb_z + 1.3])
            cube([8.4, 20, 4.0], center = true);

    // 3.5 mm TRRS
    translate([pi_ox + 54.0, pi_oy - 6, pcb_z + 2.4])
        rotate([-90, 0, 0])
            cylinder(d = 7.2, h = 16);

    // Dual USB 3.0  (near HDMI edge)
    translate([pi_ox + pi_x, pi_oy + 8.8, pcb_top + 8.0])
        cube([20, 16.8, 16.8], center = true);

    // Dual USB 2.0
    translate([pi_ox + pi_x, pi_oy + 26.8, pcb_top + 8.0])
        cube([20, 16.8, 16.8], center = true);

    // RJ45 Ethernet  (near GPIO edge)  centre Y = 45.75
    translate([pi_ox + pi_x, pi_oy + 45.75, pcb_top + 6.8])
        cube([20, 16.6, 14.2], center = true);

    // microSD  (left / SD edge, underside of PCB)
    translate([pi_ox, pi_oy + 28.0, pcb_z - 0.4])
        cube([24, 14.0, 3.0], center = true);
}

module side_vents() {
    if (vent_enable) {
        // GPIO-end wall, below the SMA zone
        translate([inner_x * 0.22, inner_y - 1, floor_t + 7])
            for (i = [0 : 5])
                translate([i * 9, 0, 0])
                    cube([5.5, wall + 4, 2.2]);

        // HDMI-end wall, between USB-C and the corner
        translate([pi_ox + 62, -wall - 1, floor_t + 8])
            for (i = [0 : 3])
                translate([i * 7.5, 0, 0])
                    cube([4.5, wall + 4, 2.2]);
    }
}

/* ---------------------------------- base ----------------------------------- */

module standoffs() {
    for (h = pi_holes) {
        translate([pi_ox + h[0], pi_oy + h[1], floor_t])
            difference() {
                cylinder(d = 6.8, h = standoff_h);
                translate([0, 0, -eps])
                    cylinder(d = 2.75, h = standoff_h + 1);
            }
    }
}

module lid_posts() {
    for (p = lid_screws) {
        translate([p[0], p[1], floor_t])
            difference() {
                cylinder(d = 7.6, h = cavity_h - 0.4);
                translate([0, 0, -eps])
                    cylinder(d = 2.55, h = cavity_h + 1);   // M3 self-taps into PETG
            }
    }
}

module base_body() {
    difference() {
        union() {
            difference() {
                linear_extrude(base_h) outer_2d();
                translate([0, 0, floor_t])
                    linear_extrude(cavity_h + 1)
                        inner_2d();
            }
            standoffs();
            lid_posts();
        }

        pi_port_cutouts();
        side_vents();

        // Through-holes + M2.5 nut traps for the Pi / HAT stack
        for (h = pi_holes) {
            translate([pi_ox + h[0], pi_oy + h[1], -eps])
                cylinder(d = 2.75, h = floor_t + standoff_h + 1);
            translate([pi_ox + h[0], pi_oy + h[1], -eps])
                hex_af(5.4, 2.3);
        }

        // Lid screw pilots continue through the post into the floor (optional)
        for (p = lid_screws)
            translate([p[0], p[1], floor_t + 4])
                cylinder(d = 2.55, h = cavity_h);

        // Rubber feet
        for (p = [[8, 8], [inner_x - 8, 8], [8, inner_y - 8], [inner_x - 8, inner_y - 8]])
            translate([p[0], p[1], -eps])
                foot_recess();

        // Wall-mount keyholes on the GPIO margin (open this side against the wall)
        if (wall_mount) {
            for (x = [inner_x * 0.28, inner_x * 0.72]) {
                translate([x, inner_y - 7.0, -eps]) {
                    cylinder(d = 8.2, h = 1.6);
                    hull() {
                        cylinder(d = 4.4, h = floor_t + 1);
                        translate([0, -7, 0])
                            cylinder(d = 4.4, h = floor_t + 1);
                    }
                }
            }
        }
    }
}

module base_print() {
    translate([wall, wall, 0]) base_body();
}

/* ----------------------------------- lid ----------------------------------- */

module lid_body() {
    difference() {
        union() {
            // Alignment lip (sits down inside the base)
            linear_extrude(lip_h)
                offset(delta = -(lip_t + fit_clear))
                    inner_2d();

            // Outer cap
            translate([0, 0, lip_h])
                linear_extrude(lid_t)
                    outer_2d();

            // SMA seating rings on the outside (do not add thickness in the hole)
            if (sma_enable) {
                for (x = [sma_x1, sma_x2])
                    translate([x, sma_y, lip_h + lid_t])
                        difference() {
                            cylinder(d = 12.5, h = 0.6);
                            translate([0, 0, -eps])
                                cylinder(d = 8.4, h = 1.0);
                        }
            }
        }

        // Hollow the lip so it is a ring, not a solid plug
        translate([0, 0, -eps])
            linear_extrude(lip_h + eps)
                offset(delta = -(lip_t + fit_clear + 1.15))
                    inner_2d();

        // Lid screw holes (M3 clearance) + shallow heads
        for (p = lid_screws) {
            translate([p[0], p[1], -1])
                cylinder(d = 3.3, h = lip_h + lid_t + 4);
            translate([p[0], p[1], lip_h + lid_t - 1.3])
                cylinder(d = 6.2, h = 3);
        }

        // MAIN + AUX SMA D-holes and nut counterbores (inside)
        if (sma_enable) {
            for (x = [sma_x1, sma_x2]) {
                translate([x, sma_y, -1])
                    sma_d_hole(lip_h + lid_t + 4);
                translate([x, sma_y, lip_h - 0.2])
                    cylinder(d = 10.2, h = 1.5);           // nut / washer pocket
            }
        }

        // GNSS knockout: 0.45 mm membrane, punch if you fit a GPS antenna
        if (gps_knockout) {
            translate([gps_x, gps_y, -1])
                cylinder(d = 6.5, h = lip_h + lid_t - 0.45 + 1);
        }

        // Vent grill over the Pi SoC / HAT
        if (vent_enable) {
            translate([pi_ox + 18, pi_oy + 10, lip_h - 0.2])
                grill(40, 28, 1.3, 2.1, lid_t + 2);
        }

        // PWR / NET status LEDs (generic HAT positions — tweak if needed)
        translate([pi_ox + 48, pi_oy + 8, -1])
            cylinder(d = 2.4, h = lip_h + lid_t + 3);
        translate([pi_ox + 54, pi_oy + 8, -1])
            cylinder(d = 2.4, h = lip_h + lid_t + 3);
    }

    // Raised MAIN / AUX captions on the outer face (print the lid lip-down)
    if (sma_enable) {
        for (spec = [[sma_x1, "MAIN"], [sma_x2, "AUX"]])
            translate([spec[0], sma_y - 11, lip_h + lid_t - 0.45])
                linear_extrude(0.5)
                    text(spec[1], size = 3.2, halign = "center", valign = "center",
                         font = "Arial:style=Bold");
        translate([inner_x / 2, 10, lip_h + lid_t - 0.45])
            linear_extrude(0.5)
                text(label_text, size = 6, halign = "center", valign = "center",
                     font = "Arial:style=Bold");
    }
}

module lid_print() {
    translate([wall, wall, 0]) lid_body();
}

/* ----------------------- preview dummies (not printed) --------------------- */

module pi_dummy() {
    translate([pi_ox, pi_oy, pcb_z]) {
        color("#3d8c40")
            difference() {
                translate([0, 0, 0]) cube([pi_x, pi_y, pcb_t]);
                for (h = pi_holes)
                    translate([h[0], h[1], -1]) cylinder(d = 2.7, h = 4);
            }
        // USB / ETH blocks
        color("#c0c0c0") {
            translate([pi_x - 1, 1.0, pcb_t])   cube([18.5, 15.4, 16.0]);
            translate([pi_x - 1, 19.1, pcb_t])  cube([18.5, 15.4, 16.0]);
            translate([pi_x - 1, 37.6, pcb_t])  cube([16.5, 16.0, 13.6]);
        }
        color("#222")
            translate([7, pi_y - 5.2, pcb_t]) cube([51, 5.1, 8.5]);  // GPIO
    }
}

module hat_dummy() {
    // Standard HAT aligned to the SD / GPIO end of the Pi
    translate([pi_ox, pi_oy, pcb_top + 8.5]) {
        color("#1f4e8c") cube([65.15, 56.21, 1.6]);
        color("#555") translate([18, 12, 1.6]) cube([30, 30, 8]);     // SIM7600 can
    }
}

module paddle(h = 165, w = 22, t = 8) {
    hull() {
        translate([0, 0, 8]) cube([t, 9, 1], center = true);
        translate([0, 0, h]) cube([t, w, 1], center = true);
    }
}

module antenna_dummies() {
    color("#1a1a1a")
        for (x = [sma_x1, sma_x2])
            translate([x, sma_y, base_h + lid_t]) {
                cylinder(d = 8, h = 10);
                translate([0, 0, 10]) paddle();
            }
}

module preview() {
    translate([wall, wall, 0]) {
        color("#2b2b2b", 0.92) base_body();
        color("#3a3a3a", 0.92) translate([0, 0, base_h - lip_h]) lid_body();
        pi_dummy();
        hat_dummy();
        if (sma_enable) antenna_dummies();
    }

    echo("==============================================");
    echo(str("Inner cavity : ", inner_x, " x ", inner_y, " x ", cavity_h, " mm"));
    echo(str("Outer body   : ", outer_x, " x ", outer_y, " x ", outer_z, " mm"));
    echo(str("Pi origin    : (", pi_ox, ", ", pi_oy, ", ", pcb_z, ")"));
    echo(str("SMA MAIN/AUX : x=", sma_x1, " / ", sma_x2, "  y=", sma_y));
    echo("==============================================");
}

module print_set() {
    base_print();
    translate([outer_x + 10, 0, 0]) lid_print();
}

/* --------------------------------- render ---------------------------------- */

if (part == "base")      base_print();
else if (part == "lid")  lid_print();
else if (part == "print_set") print_set();
else preview();
