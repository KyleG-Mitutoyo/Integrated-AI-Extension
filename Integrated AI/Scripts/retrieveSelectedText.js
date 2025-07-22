// Integrated AI
// Copyright (C) 2025 Kyle Grubbs

// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any other later version.

// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.

// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <https://www.gnu.org/licenses/>.

function retrieveSelectedText() {
    try {
        let selectedText = window.getSelection().toString();
        if (!selectedText) {
            return 'null';
        }
        return selectedText;
    } catch (error) {
        console.error('retrieveSelectedText ERROR: ' + error.message);
        return 'FAILURE: ' + error.message;
    }
}